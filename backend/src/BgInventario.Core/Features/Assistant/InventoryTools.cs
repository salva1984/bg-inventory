using System.ComponentModel;
using System.Linq.Expressions;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Assistant;

public sealed record ProductStockSummary
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int TotalStock { get; init; }
    public int SupplierCount { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
}

public sealed record SupplierOffer(string SupplierName, decimal LowestPrice, int Stock);

public sealed record SupplierPriceComparison(string Code, string Name, IReadOnlyList<SupplierOffer> Offers);

// Read-only queries the model can call; the [Description] texts are what it reads to pick a tool.
public sealed class InventoryTools(IAppDbContext db)
{
    internal const int MaxResults = 20;

    internal const int DefaultLowStockThreshold = 10;

    [Description("Busca productos por código o por palabras de su nombre (deben aparecer todas, en cualquier orden). Devuelve el código, el stock total, cuántos proveedores lo venden y el rango de precios.")]
    public async Task<IReadOnlyList<ProductStockSummary>> SearchProductsAsync(
        [Description("Código del producto o palabras de su nombre, por ejemplo \"cable hdmi\".")] string text,
        CancellationToken ct)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return [];
        }

        var products = db.Products.AsNoTracking();
        foreach (var word in words)
        {
            products = products.Where(p => p.Code.Contains(word) || p.Name.Contains(word));
        }

        return await products
            .Select(ToSummary)
            .OrderBy(p => p.Name)
            .Take(MaxResults)
            .ToListAsync(ct);
    }

    [Description("Lista los productos cuyo stock total es igual o menor al umbral, del que tiene menos al que tiene más. Sirve para saber qué reponer.")]
    public async Task<IReadOnlyList<ProductStockSummary>> GetLowStockProductsAsync(
        [Description("Stock máximo a incluir. Si el usuario no indica uno, usa 10.")] int threshold = DefaultLowStockThreshold,
        CancellationToken ct = default)
    {
        return await db.Products.AsNoTracking()
            .Select(ToSummary)
            .Where(p => p.TotalStock <= threshold)
            .OrderBy(p => p.TotalStock)
            .ThenBy(p => p.Name)
            .Take(MaxResults)
            .ToListAsync(ct);
    }

    [Description("Compara el precio de un producto entre sus proveedores, del más barato al más caro, con el stock de cada uno. Necesita el código exacto: si solo tienes el nombre, búscalo antes con search_products. Devuelve null si el código no existe.")]
    public async Task<SupplierPriceComparison?> CompareSupplierPricesAsync(
        [Description("Código exacto del producto, tal como lo devuelve search_products.")] string productCode,
        CancellationToken ct)
    {
        var code = ProductService.NormalizeCode(productCode);
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Code == code)
            .Select(p => new { p.Id, p.Code, p.Name })
            .FirstOrDefaultAsync(ct);
        if (product is null)
        {
            return null;
        }

        var bySupplier = await db.InventoryLots.AsNoTracking()
            .Where(lot => lot.ProductId == product.Id)
            .GroupBy(lot => new { lot.SupplierId, lot.Supplier.Name })
            .Select(g => new
            {
                g.Key.Name,
                LowestPrice = g.Min(lot => lot.Price),
                Stock = g.Sum(lot => lot.Stock)
            })
            .ToListAsync(ct);

        // In memory because SQLite (the tests) cannot ORDER BY a decimal.
        var offers = bySupplier
            .OrderBy(s => s.LowestPrice)
            .ThenBy(s => s.Name)
            .Select(s => new SupplierOffer(s.Name, s.LowestPrice, s.Stock))
            .ToList();

        return new SupplierPriceComparison(product.Code, product.Name, offers);
    }

    // Member-init, not a positional record, so EF can still filter and sort over it.
    private static readonly Expression<Func<Product, ProductStockSummary>> ToSummary = p => new ProductStockSummary
    {
        Code = p.Code,
        Name = p.Name,
        TotalStock = p.Lots.Sum(lot => (int?)lot.Stock) ?? 0,
        SupplierCount = p.Lots.Select(lot => lot.SupplierId).Distinct().Count(),
        MinPrice = p.Lots.Min(lot => (decimal?)lot.Price),
        MaxPrice = p.Lots.Max(lot => (decimal?)lot.Price)
    };
}
