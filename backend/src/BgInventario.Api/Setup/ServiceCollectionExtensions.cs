using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BgInventario.Core.Entities;
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

/// <summary>
/// JSON, validation, error format, CORS, JWT, rate limiting, Swagger, health checks and telemetry.
/// </summary>
public static class ServiceCollectionExtensions
{
    public const string CorsPolicy = "Frontend";
    public const string AdminPolicy = "Admin";
    public const string LoginRateLimit = "login";

    // Name under which logs, traces and metrics appear in the dashboard (OTLP "service.name").
    public const string ServiceName = "BgInventario.Api";

    // Brute-force protection: login attempts allowed per client IP in each window.
    private const int LoginAttemptsPerWindow = 5;
    private static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(1);

    // How long the browser may cache a CORS preflight answer before asking again.
    private static readonly TimeSpan PreflightCacheDuration = TimeSpan.FromMinutes(10);

    // Tolerance for clock differences between the server that issues the token and the one that validates it.
    private static readonly TimeSpan TokenClockSkew = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // DataAnnotations on the request records
        services.AddValidation();

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddCors(configuration);
        services.AddJwtAuthentication(configuration);
        services.AddLoginRateLimit();
        services.AddSwagger();

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
        // Any authenticated user can read; only Admin can write.
        services.AddAuthorizationBuilder()
            //  every endpoint requires a token unless it declares AllowAnonymous.
            .SetFallbackPolicy(requireAuthPolicy)
            // AdminPolicy is used in the endpoints that require an Admin role.
            .AddPolicy(AdminPolicy, p => p.RequireRole(nameof(UserRole.Admin)));
    }

    private static void AddLoginRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // One counter per client IP: one attacker cannot lock out every other user.
            options.AddPolicy(LoginRateLimit, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _loginWindow => new FixedWindowRateLimiterOptions { 
                    PermitLimit = LoginAttemptsPerWindow, Window = LoginWindow }));
        });

    // Traces (each request with its SQL queries) and metrics, exported over OTLP. The destination
    // comes from OTEL_EXPORTER_OTLP_ENDPOINT (default http://localhost:4317); logs reach the same
    // place through Serilog's OpenTelemetry sink.
    private static void AddTelemetry(this IServiceCollection services) =>
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddSqlClientInstrumentation()
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
            // Swagger UI will send the token in the Authorization header for all requests.
            o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
    }
}
