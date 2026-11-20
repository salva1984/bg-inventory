using BgInventario.Core.Features.Dashboard;
using BgInventario.Core.Features.Products;
using BgInventario.Core.Features.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Tests;

public sealed class DashboardServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ProductService _products;
    private readonly SupplierService _suppliers;
    private readonly DashboardService _service;

    public DashboardServiceTests()
    {
        _products = new ProductService(_db.Context, TimeProvider.System, _db.DashboardCache);
        _suppliers = new SupplierService(_db.Context, TimeProvider.System, _db.DashboardCache);
        _service = new DashboardService(_db.Context, _db.DashboardCache);
    }

    [Fact]
    public async Task Totals_sum_units_and_value_over_every_lot()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("P1", "Monitor", [new(null, a.Id, "L1", 250m, 2), new(null, b.Id, "L2", 300m, 1)]);
        await CreateProductAsync("P2", "Parlante", [new(null, a.Id, "L3", 100m, 0)]);
        await CreateProductAsync("P3", "Teclado", []);

        var totals = (await _service.GetSummaryAsync(default)).Totals;

        Assert.Equal(3, totals.ProductCount);
        Assert.Equal(2, totals.SupplierCount);
        // Parlante tiene un lote con 0 unidades y Teclado no tiene lotes: los dos están sin stock.
        Assert.Equal(2, totals.OutOfStockProductCount);
        Assert.Equal(3, totals.TotalUnits);
        Assert.Equal(250m * 2 + 300m * 1, totals.InventoryValue);
    }

    [Fact]
    public async Task Value_by_supplier_is_ranked_from_highest_value()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("P1", "Monitor", [new(null, a.Id, "L1", 10m, 1), new(null, b.Id, "L2", 10m, 5)]);

        var bySupplier = (await _service.GetSummaryAsync(default)).ValueBySupplier;

        Assert.Equal([b.Id, a.Id], bySupplier.Select(s => s.SupplierId));
        Assert.Equal(50m, bySupplier[0].InventoryValue);
    }

    [Fact]
    public async Task Price_spread_skips_products_with_a_single_price_and_ranks_by_relative_gap()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        // Monitor: 200 → 300 es un 50 % más caro. Parlante: 100 → 200 es un 100 % más caro.
        await CreateProductAsync("P1", "Monitor", [new(null, a.Id, "L1", 200m, 1), new(null, b.Id, "L2", 300m, 1)]);
        await CreateProductAsync("P2", "Parlante", [new(null, a.Id, "L3", 100m, 1), new(null, b.Id, "L4", 200m, 1)]);
        await CreateProductAsync("P3", "Teclado", [new(null, a.Id, "L5", 50m, 1), new(null, b.Id, "L6", 50m, 1)]);

        var spread = (await _service.GetSummaryAsync(default)).LargestPriceSpread;

        Assert.Equal(["Parlante", "Monitor"], spread.Select(p => p.Name));
        Assert.Equal(100m, spread[0].SpreadPercent);
    }

    [Fact]
    public async Task Deleted_products_are_left_out()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var deleted = await CreateProductAsync("P1", "Monitor", [new(null, a.Id, "L1", 10m, 4)]);
        await _products.DeleteAsync(deleted, default);

        var summary = await _service.GetSummaryAsync(default);

        Assert.Equal(0, summary.Totals.ProductCount);
        Assert.Equal(0, summary.Totals.TotalUnits);
        Assert.Empty(summary.LowestStock);
    }

    [Fact]
    public async Task Summary_is_cached_until_a_product_changes()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("P1", "Monitor", [new(null, a.Id, "L1", 10m, 1)]);
        await _service.GetSummaryAsync(default);

        // Un cambio que no pasa por los servicios (por ejemplo, un script en la base) no se ve
        // hasta que el caché vence: confirma que la segunda lectura no volvió a consultar.
        await using (var outsideChange = _db.CreateContext())
        {
            var lot = await outsideChange.InventoryLots.SingleAsync();
            lot.Stock = 100;
            await outsideChange.SaveChangesAsync();
        }
        var cached = await _service.GetSummaryAsync(default);
        Assert.Equal(1, cached.Totals.TotalUnits);

        // Guardar un producto invalida el resumen y la siguiente lectura trae los datos actuales.
        await CreateProductAsync("P2", "Parlante", [new(null, a.Id, "L2", 10m, 5)]);
        var fresh = await _service.GetSummaryAsync(default);
        Assert.Equal(105, fresh.Totals.TotalUnits);
    }

    [Fact]
    public async Task Renaming_a_supplier_invalidates_the_summary()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("P1", "Monitor", [new(null, a.Id, "L1", 10m, 1)]);
        await _service.GetSummaryAsync(default);

        await _suppliers.UpdateAsync(a.Id, new SaveSupplierRequest("Proveedor Renombrado", null, null), default);
        var summary = await _service.GetSummaryAsync(default);

        Assert.Equal("Proveedor Renombrado", summary.ValueBySupplier.Single().SupplierName);
    }

    private async Task<int> CreateProductAsync(string code, string name, SaveInventoryLotRequest[] lots)
    {
        var result = await _products.CreateAsync(new SaveProductRequest(code, name, null, lots), default);
        return result.Value.Id;
    }

    public void Dispose() => _db.Dispose();
}
