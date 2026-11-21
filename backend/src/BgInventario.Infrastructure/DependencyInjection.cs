using System.ClientModel;
using BgInventario.Core.Common;
using BgInventario.Core.Features.Assistant;
using BgInventario.Core.Features.Auth;
using BgInventario.Infrastructure.Assistant;
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
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>())
            .UseAsyncSeeding((ctx, _, ct) => Seed(ctx, sp, ct))
            .UseSeeding((ctx, _) => Seed(ctx, sp, CancellationToken.None).GetAwaiter().GetResult()));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddInvoiceExtraction(configuration);
        services.AddInventoryAssistant(configuration);

        return services;
    }

    private static void AddInvoiceExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(InvoiceExtractionOptions.SectionName).Get<InvoiceExtractionOptions>()
            ?? new InvoiceExtractionOptions();

        if (!options.IsConfigured)
        {
            services.AddSingleton<IInvoiceExtractor, UnconfiguredInvoiceExtractor>();
            return;
        }

        var chatClient = CreateChatClient(
            options.Endpoint, options.Model, options.ApiKey!, options.Timeout, InvoiceExtractionTelemetry.SourceName);

        services.AddSingleton<IInvoiceExtractor>(sp => new LlmInvoiceExtractor(
            chatClient, options, sp.GetRequiredService<ILogger<LlmInvoiceExtractor>>()));
    }

    /// <summary>Scoped because its tools use the request's DbContext; the model client is shared.</summary>
    private static void AddInventoryAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AssistantOptions.SectionName).Get<AssistantOptions>()
            ?? new AssistantOptions();

        if (!options.IsConfigured)
        {
            services.AddSingleton<IInventoryAssistant, UnconfiguredInventoryAssistant>();
            return;
        }

        var chatClient = CreateChatClient(
            options.Endpoint, options.Model, options.ApiKey!, options.Timeout, AssistantTelemetry.SourceName);

        services.AddScoped<IInventoryAssistant>(sp => new LlmInventoryAssistant(
            chatClient,
            options,
            sp.GetRequiredService<InventoryTools>(),
            sp.GetRequiredService<ILogger<LlmInventoryAssistant>>()));
    }

    /// <summary>Client for an OpenAI-compatible API (OpenRouter), with a trace span per model call.</summary>
    private static IChatClient CreateChatClient(
        string endpoint, string model, string apiKey, TimeSpan timeout, string telemetrySourceName)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = timeout
        };

        return new ChatClient(model, new ApiKeyCredential(apiKey), clientOptions)
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: telemetrySourceName)
            .Build();
    }

    private static Task Seed(DbContext context, IServiceProvider sp, CancellationToken ct) =>
        DbSeeder.SeedAsync(
            context,
            sp.GetRequiredService<IOptions<SeedOptions>>().Value,
            sp.GetRequiredService<IPasswordHasher>(),
            ct);
}
