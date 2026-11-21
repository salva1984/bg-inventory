namespace BgInventario.Core.Features.Dashboard;

public sealed record DashboardSummary(
    DashboardTotals Totals,
    IReadOnlyList<SupplierInventoryValue> ValueBySupplier,
    IReadOnlyList<LowStockProduct> LowestStock,
    IReadOnlyList<PriceSpreadProduct> LargestPriceSpread);

public sealed record DashboardTotals(
    int ProductCount,
    int SupplierCount,
    int OutOfStockProductCount,
    long TotalUnits,
    decimal InventoryValue);

public sealed record SupplierInventoryValue(int SupplierId, string SupplierName, decimal InventoryValue, long Units);

public sealed record LowStockProduct(int Id, string Code, string Name, int TotalStock);

public sealed record PriceSpreadProduct(
    int Id,
    string Code,
    string Name,
    decimal MinPrice,
    decimal MaxPrice,
    decimal SpreadPercent);
