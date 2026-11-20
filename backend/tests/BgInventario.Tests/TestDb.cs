using BgInventario.Core.Entities;
using BgInventario.Core.Features.Dashboard;
using BgInventario.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace BgInventario.Tests;

/// <summary>Real relational database (SQLite in memory): tests constraints and LINQ translation, not mocks.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDb()
    {
        _connection.Open();
        Context = CreateContext();
        Context.Database.EnsureCreated();
    }

    public AppDbContext Context { get; }

    /// <summary>Real HybridCache, empty for each test and shared by the services of that test.</summary>
    public DashboardCache DashboardCache { get; } = CreateDashboardCache();

    private static DashboardCache CreateDashboardCache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        var cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();
        return new DashboardCache(cache);
    }

    /// <summary>Fresh context over the same database (no tracked entities), to assert what was persisted.</summary>
    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(_connection)
        .AddInterceptors(new AuditInterceptor(TimeProvider.System))
        .Options);

    public async Task<(Supplier A, Supplier B)> SeedSuppliersAsync()
    {
        var a = new Supplier { Name = "Proveedor A" };
        var b = new Supplier { Name = "Proveedor B" };
        Context.Suppliers.AddRange(a, b);
        await Context.SaveChangesAsync();
        return (a, b);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
