// Rate limiter after auth: it needs the signed-in user, and invalid tokens never spend quota.
using BgInventario.Api.Endpoints;
using BgInventario.Api.Setup;
using BgInventario.Core;
using BgInventario.Infrastructure;
using BgInventario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Logs errors that happen before the host is built.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, config) => config
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.OpenTelemetry(options => options.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = ServiceCollectionExtensions.ServiceName
        }));

    builder.Services
        .AddCore()
        .AddInfrastructure(builder.Configuration)
        .AddApi(builder.Configuration);

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages(); // empty 401/403/404 responses also become ProblemDetails

    app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diag, http) =>
    {
        diag.Set("UserEmail", http.User.Identity?.Name ?? "anonymous");
        diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
    });

    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
    {
        app.UseSwagger();
        app.UseSwaggerUI(o => o.DocumentTitle = "BgInventario API");
    }

    app.UseCors(ServiceCollectionExtensions.CorsPolicy);
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapAuthEndpoints();
    app.MapProductEndpoints();
    app.MapSupplierEndpoints();
    app.MapDashboardEndpoints();
    app.MapClientLogEndpoints();
    app.MapInvoiceEndpoints();
    app.MapAssistantEndpoints();

    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(); // also runs the EF seeding (UseAsyncSeeding)
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Exposed for integration tests (WebApplicationFactory).
public partial class Program;
