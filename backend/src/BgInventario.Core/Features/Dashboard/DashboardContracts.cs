// Response shape of the dashboard: inventory totals and short ranked lists, all computed in SQL.
namespace BgInventario.Core.Features.Dashboard;

public sealed record DashboardSummary(
    DashboardTotals Totals,
    IReadOnlyList<SupplierInventoryValue> ValueBySupplier,
    IReadOnlyList<LowStockProduct> LowestStock,
    IReadOnlyList<PriceSpreadProduct> LargestPriceSpread);

/// <summary>Headline numbers. InventoryValue is the sum of price × stock over every lot.</summary>
public sealed record DashboardTotals(
    int ProductCount,
    int SupplierCount,
    int OutOfStockProductCount,
    long TotalUnits,
    decimal InventoryValue);

/// <summary>How much of the inventory value comes from each supplier.</summary>
public sealed record SupplierInventoryValue(int SupplierId, string SupplierName, decimal InventoryValue, long Units);

public sealed record LowStockProduct(int Id, string Code, string Name, int TotalStock);

/// <summary>
/// Same product, different price depending on the supplier or lot: the gap between the cheapest
/// and the most expensive lot. SpreadPercent is relative to the lowest price.
/// </summary>
public sealed record PriceSpreadProduct(
    int Id,
    string Code,
    string Name,
    decimal MinPrice,
    decimal MaxPrice,
    decimal SpreadPercent);
