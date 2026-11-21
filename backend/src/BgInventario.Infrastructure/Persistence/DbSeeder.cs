using BgInventario.Core.Entities;
using BgInventario.Core.Features.Auth;
using BgInventario.Core.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; init; } = "admin@inventario.local";
    public string? AdminPassword { get; init; }
    public string ViewerEmail { get; init; } = "viewer@inventario.local";
    public string? ViewerPassword { get; init; }
    public bool SampleData { get; init; }

    public int SampleProductCount { get; init; }
}

internal static class DbSeeder
{
    public static async Task SeedAsync(DbContext context, SeedOptions options, IPasswordHasher hasher, CancellationToken ct)
    {
        var db = (AppDbContext)context;

        if (!await db.Users.AnyAsync(ct))
        {
            if (!string.IsNullOrWhiteSpace(options.AdminPassword))
            {
                db.Users.Add(new User
                {
                    Email = options.AdminEmail.ToLowerInvariant(),
                    FullName = "Administrador",
                    PasswordHash = hasher.Hash(options.AdminPassword),
                    Role = UserRole.Admin
                });
            }

            if (!string.IsNullOrWhiteSpace(options.ViewerPassword))
            {
                db.Users.Add(new User
                {
                    Email = options.ViewerEmail.ToLowerInvariant(),
                    FullName = "Consulta",
                    PasswordHash = hasher.Hash(options.ViewerPassword),
                    Role = UserRole.Viewer
                });
            }

            await db.SaveChangesAsync(ct);
        }

        if (options.SampleData && !await db.Suppliers.AnyAsync(ct))
        {
            var a = new Supplier { Name = "Tecnología del Guayas S.A.", ContactEmail = "ventas@tecnoguayas.ec", Phone = "042000001" };
            var b = new Supplier { Name = "Compudistribuciones del Litoral Cía. Ltda.", ContactEmail = "ventas@compulitoral.ec", Phone = "042000002" };
            var n = new Supplier { Name = "Electroimportadora Austral S.A.", ContactEmail = "ventas@electroaustral.com.ec", Phone = "042000003" };

            db.Products.AddRange(
                Product("MON-50-4K", "Monitor 50 pulgadas 4K", "Monitor UHD de 50\"",
                    (a, "L-2026-01", 250m, 12), (b, "L-2026-01", 300m, 5), (n, "L-2026-01", 200m, 8)),
                Product("SND-20000", "Equipo de Sonido 20000", "Equipo de sonido 20000 W",
                    (a, "L-2026-01", 150m, 20), (b, "L-2026-01", 200m, 4), (n, "L-2026-01", 100m, 10), (n, "L-2026-02", 110m, 6)),
                Product("LAP-14-I7", "Laptop 14\" Core i7", null,
                    (a, "L-2026-02", 900m, 3), (b, "L-2026-02", 950m, 7)),
                Product("TEC-MEC-ES", "Teclado mecánico español", null,
                    (b, "L-001", 45m, 40), (n, "L-001", 39.90m, 25)),
                Product("MOU-INAL", "Mouse inalámbrico", null,
                    (a, "L-2026-03", 12.50m, 100)));

            if (options.SampleProductCount > 0)
            {
                var generated = SampleDataGenerator.Generate(options.SampleProductCount, [a, b, n]);
                db.Suppliers.AddRange(generated.Suppliers);
                db.Products.AddRange(generated.Products);
            }

            await db.SaveChangesAsync(ct);
        }

        await IndexProductsWithoutTrigramsAsync(db, ct);
    }

    private static async Task IndexProductsWithoutTrigramsAsync(AppDbContext db, CancellationToken ct)
    {
        var unindexed = await db.Products
            .Include(p => p.Trigrams)
            .Where(p => !p.Trigrams.Any())
            .ToListAsync(ct);
        if (unindexed.Count == 0)
        {
            return;
        }

        unindexed.ForEach(ProductSearchIndex.Refresh);
        await db.SaveChangesAsync(ct);
    }

    private static Product Product(
        string code, string name, string? description,
        params (Supplier Supplier, string Lot, decimal Price, int Stock)[] lots) => new()
    {
        Code = code,
        Name = name,
        Description = description,
        Lots = lots
            .Select(l => new InventoryLot { Supplier = l.Supplier, LotNumber = l.Lot, Price = l.Price, Stock = l.Stock })
            .ToList()
    };
}
