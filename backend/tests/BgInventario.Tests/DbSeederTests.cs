using BgInventario.Core.Entities;
using BgInventario.Infrastructure.Persistence;
using BgInventario.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Tests;

/// <summary>The sample data (fixed + generated with Bogus) must fit the real constraints and be reproducible.</summary>
public sealed class DbSeederTests : IDisposable
{
    private const int GeneratedProducts = 300;

    // The fixed sample data of the challenge: 3 suppliers and 5 products.
    private const int FixedSuppliers = 3;
    private const int FixedProducts = 5;

    private readonly TestDb _db = new();
    private readonly SeedOptions _options = new() { SampleData = true, SampleProductCount = GeneratedProducts };

    [Fact]
    public async Task Seed_inserts_fixed_and_generated_data()
    {
        await DbSeeder.SeedAsync(_db.Context, _options, new Pbkdf2PasswordHasher(), default);

        await using var check = _db.CreateContext();
        Assert.Equal(FixedProducts + GeneratedProducts, await check.Products.CountAsync());
        Assert.Equal(FixedSuppliers + SampleCatalog.Suppliers.Length, await check.Suppliers.CountAsync());
        Assert.True(await check.Products.AllAsync(p => p.Lots.Any()));
        Assert.True(await check.Products.AnyAsync(p => p.Code == "MON-50-4K"));
    }

    [Fact]
    public async Task Seed_is_reproducible_and_idempotent()
    {
        await DbSeeder.SeedAsync(_db.Context, _options, new Pbkdf2PasswordHasher(), default);
        await DbSeeder.SeedAsync(_db.Context, _options, new Pbkdf2PasswordHasher(), default);

        await using var check = _db.CreateContext();
        Assert.Equal(FixedProducts + GeneratedProducts, await check.Products.CountAsync());

        // Same fixed seed → same first generated product as a fresh generation.
        // The pool needs the three fixed suppliers, as in the seeder, so there are enough to pick lots from.
        var fixedSuppliers = Enumerable.Range(1, FixedSuppliers).Select(_ => new Supplier { Name = "Fijo" }).ToList();
        var expected = SampleDataGenerator.Generate(GeneratedProducts, fixedSuppliers).Products[0];
        var actual = await check.Products.SingleAsync(p => p.Code == expected.Code);
        Assert.Equal(expected.Name, actual.Name);
    }

    public void Dispose() => _db.Dispose();
}
