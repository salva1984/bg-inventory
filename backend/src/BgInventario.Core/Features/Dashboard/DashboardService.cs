using BgInventario.Core.Common;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Dashboard;

public sealed class DashboardService(IAppDbContext db, DashboardCache cache)
{
    private const int TopSuppliers = 8;
    private const int TopProducts = 5;

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(async token => await ComputeSummaryAsync(token), ct);

    // The queries run one after another: a DbContext does not allow parallel queries.
    private async Task<DashboardSummary> ComputeSummaryAsync(CancellationToken ct)
    {
        var totals = await GetTotalsAsync(ct);
        var valueBySupplier = await GetValueBySupplierAsync(ct);
        var lowestStock = await GetLowestStockAsync(ct);
        var largestPriceSpread = await GetLargestPriceSpreadAsync(ct);
        return new DashboardSummary(totals, valueBySupplier, lowestStock, largestPriceSpread);
    }

    private async Task<DashboardTotals> GetTotalsAsync(CancellationToken ct)
    {
        var productCount = await db.Products.CountAsync(ct);
        var supplierCount = await db.Suppliers.CountAsync(ct);
        var outOfStockCount = await db.Products.CountAsync(p => (p.Lots.Sum(lot => (int?)lot.Stock) ?? 0) == 0, ct);
        // long: the sum of many int stocks can exceed int.MaxValue.
        var totalUnits = await db.InventoryLots.SumAsync(lot => (long)lot.Stock, ct);
        var inventoryValue = await db.InventoryLots.SumAsync(lot => lot.Price * lot.Stock, ct);

        return new DashboardTotals(productCount, supplierCount, outOfStockCount, totalUnits, inventoryValue);
    }

    // The lists are sorted over an anonymous type and mapped to the record afterwards:
    // EF cannot translate an ORDER BY over a member of a positional record.
    private async Task<List<SupplierInventoryValue>> GetValueBySupplierAsync(CancellationToken ct)
    {
        var ranked = await db.InventoryLots.AsNoTracking()
            .GroupBy(lot => new { lot.SupplierId, lot.Supplier.Name })
            .Select(g => new
            {
                g.Key.SupplierId,
                g.Key.Name,
                InventoryValue = g.Sum(lot => lot.Price * lot.Stock),
                Units = g.Sum(lot => (long)lot.Stock)
            })
            .OrderByDescending(s => s.InventoryValue)
            .ThenBy(s => s.Name)
            .Take(TopSuppliers)
            .ToListAsync(ct);

        return ranked
            .Select(s => new SupplierInventoryValue(s.SupplierId, s.Name, s.InventoryValue, s.Units))
            .ToList();
    }

    private async Task<List<LowStockProduct>> GetLowestStockAsync(CancellationToken ct)
    {
        var ranked = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Code, p.Name, TotalStock = p.Lots.Sum(lot => (int?)lot.Stock) ?? 0 })
            .OrderBy(p => p.TotalStock)
            .ThenBy(p => p.Name)
            .Take(TopProducts)
            .ToListAsync(ct);

        return ranked
            .Select(p => new LowStockProduct(p.Id, p.Code, p.Name, p.TotalStock))
            .ToList();
    }

    private async Task<List<PriceSpreadProduct>> GetLargestPriceSpreadAsync(CancellationToken ct)
    {
        var priceRanges = db.Products.AsNoTracking()
            .Where(p => p.Lots.Count >= 2)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                MinPrice = p.Lots.Min(lot => lot.Price),
                MaxPrice = p.Lots.Max(lot => lot.Price)
            })
            .Where(p => p.MaxPrice > p.MinPrice);

        // Ranked by the relative gap: a $50 gap matters more on a $100 item than on a $2,000 one.
        // MinPrice is always > 0 (check constraint on Price), so the division is safe.
        var ranked = await priceRanges
            .OrderByDescending(p => (p.MaxPrice - p.MinPrice) / p.MinPrice)
            .ThenBy(p => p.Name)
            .Take(TopProducts)
            .ToListAsync(ct);

        return ranked
            .Select(p => new PriceSpreadProduct(
                p.Id,
                p.Code,
                p.Name,
                p.MinPrice,
                p.MaxPrice,
                SpreadPercent(p.MinPrice, p.MaxPrice)))
            .ToList();
    }

    private static decimal SpreadPercent(decimal minPrice, decimal maxPrice)
    {
        var spread = (maxPrice - minPrice) / minPrice * 100;
        return Math.Round(spread, 1);
    }
}
