// Registers persistence (SQL Server + EF Core) and security (JWT, password hashing) services.
using BgInventario.Core.Common;
using BgInventario.Core.Features.Auth;
using BgInventario.Infrastructure.Persistence;
using BgInventario.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        return services;
    }

    private static Task Seed(DbContext context, IServiceProvider sp, CancellationToken ct) =>
        DbSeeder.SeedAsync(
            context,
            sp.GetRequiredService<IOptions<SeedOptions>>().Value,
            sp.GetRequiredService<IPasswordHasher>(),
            ct);
}
