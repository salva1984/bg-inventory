using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BgInventario.Core.Entities;
using BgInventario.Infrastructure.Assistant;
using BgInventario.Infrastructure.Invoices;
using BgInventario.Infrastructure.Persistence;
using BgInventario.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BgInventario.Api.Setup;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicy = "Frontend";
    public const string AdminPolicy = "Admin";
    public const string LoginRateLimit = "login";
    public const string InvoiceAnalysisRateLimit = "invoice-analysis";

    public const string ServiceName = "BgInventario.Api";

    public const int LoginAttemptsPerWindow = 5;
    private static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(1);

    public const int InvoiceAnalysesPerWindow = 10;
    private static readonly TimeSpan InvoiceAnalysisWindow = TimeSpan.FromMinutes(1);

    public const string AssistantRateLimit = "assistant";
    public const int AssistantQuestionsPerWindow = 20;
    private static readonly TimeSpan AssistantWindow = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan PreflightCacheDuration = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan TokenClockSkew = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddValidation();

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddCors(configuration);
        services.AddJwtAuthentication(configuration);
        services.AddRateLimits();
        services.AddSwagger();

        services.AddHybridCache();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
        services.AddTelemetry();

        return services;
    }

    private static void AddCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        var policy = new CorsPolicyBuilder()
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            .SetPreflightMaxAge(PreflightCacheDuration)
            .Build();

        services.AddCors();
        services.Configure<CorsOptions>(options => options.AddPolicy(CorsPolicy, policy));
    }

    private static void AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                    ?? throw new InvalidOperationException("Falta la configuración de JWT.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.SigningKey,
                    ClockSkew = TokenClockSkew,
                    NameClaimType = JwtRegisteredClaimNames.UniqueName,
                    RoleClaimType = "role"
                };
            });
        var requireAuthPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(requireAuthPolicy)
            .AddPolicy(AdminPolicy, p => p.RequireRole(nameof(UserRole.Admin)));
    }

    private static void AddRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _loginWindow => new FixedWindowRateLimiterOptions { 
                    PermitLimit = LoginAttemptsPerWindow, Window = LoginWindow }));

            // Per signed-in user: a loop in one client cannot run up the model bill or block the others.
            options.AddPolicy(InvoiceAnalysisRateLimit, httpContext =>
                PerUserLimit(httpContext, InvoiceAnalysesPerWindow, InvoiceAnalysisWindow));
            options.AddPolicy(AssistantRateLimit, httpContext =>
                PerUserLimit(httpContext, AssistantQuestionsPerWindow, AssistantWindow));
        });

    // Needs UseRateLimiter after UseAuthentication (Program.cs); otherwise everyone
    // shares the "anonymous" counter.
    private static RateLimitPartition<string> PerUserLimit(HttpContext httpContext, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.Identity?.Name ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window });

    private static void AddTelemetry(this IServiceCollection services) =>
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddSqlClientInstrumentation()
                .AddSource(InvoiceExtractionTelemetry.SourceName) // LLM calls of the invoice analysis
                .AddSource(AssistantTelemetry.SourceName) // assistant: each model call and each tool it ran (execute_tool)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter());

    private static void AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BgInventario API",
                Version = "v1",
                Description = "Inventario de productos con precios y stock por proveedor/lote. Login: POST /api/auth/login, luego 'Authorize' con el token."
            });
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Pegue solo el token (sin 'Bearer ')."
            });
            o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
    }
}
