// Registers persistence (SQL Server + EF Core), security (JWT, password hashing) and the invoice
// extraction (LLM through an OpenAI-compatible endpoint).
using System.ClientModel;
using BgInventario.Core.Common;
using BgInventario.Core.Features.Auth;
using BgInventario.Core.Features.Invoices;
using BgInventario.Infrastructure.Invoices;
using BgInventario.Infrastructure.Persistence;
using BgInventario.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace BgInventario.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart(); // misconfiguration fails at startup, not on the first login
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<AuditInterceptor>();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured, check appsettings.");

        services.AddDbContext<AppDbContext>((sp, options) => options
            // Retries transient errors (network blips, Azure SQL failovers).
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>())
            .UseAsyncSeeding((ctx, _, ct) => Seed(ctx, sp, ct))
            .UseSeeding((ctx, _) => Seed(ctx, sp, CancellationToken.None).GetAwaiter().GetResult()));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddInvoiceExtraction(configuration);

        return services;
    }

    /// <summary>
    /// Without an API key the app still starts (local runs, tests) and only the invoice analysis
    /// answers 503; the rest of the inventory does not depend on the model.
    /// </summary>
    private static void AddInvoiceExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(InvoiceExtractionOptions.SectionName).Get<InvoiceExtractionOptions>()
            ?? new InvoiceExtractionOptions();

        if (!options.IsConfigured)
        {
            services.AddSingleton<IInvoiceExtractor, UnconfiguredInvoiceExtractor>();
            return;
        }

        // OpenRouter (and any OpenAI-compatible provider) only needs the base URL and the key.
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.Endpoint),
            NetworkTimeout = options.Timeout
        };
        var chatClient = new ChatClient(options.Model, new ApiKeyCredential(options.ApiKey!), clientOptions)
            .AsIChatClient()
            .AsBuilder()
            // Each call appears as a span (model, tokens, duration) in the same traces as the request.
            .UseOpenTelemetry(sourceName: InvoiceExtractionTelemetry.SourceName)
            .Build();

        services.AddSingleton<IInvoiceExtractor>(sp => new LlmInvoiceExtractor(
            chatClient, options, sp.GetRequiredService<ILogger<LlmInvoiceExtractor>>()));
    }

    private static Task Seed(DbContext context, IServiceProvider sp, CancellationToken ct) =>
        DbSeeder.SeedAsync(
            context,
            sp.GetRequiredService<IOptions<SeedOptions>>().Value,
            sp.GetRequiredService<IPasswordHasher>(),
            ct);
}
