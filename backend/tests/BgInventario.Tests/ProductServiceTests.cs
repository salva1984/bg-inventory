using BgInventario.Core.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Tests;

public sealed class ProductServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ProductService _service;

    public ProductServiceTests() => _service = new ProductService(_db.Context, TimeProvider.System, _db.DashboardCache);

    [Fact]
    public async Task Create_normalizes_code_and_saves_lots_per_supplier_and_lot()
    {
        var (a, b) = await _db.SeedSuppliersAsync();

        var result = await _service.CreateAsync(new SaveProductRequest(" mon-01 ", "Monitor", null,
        [
            new(null, a.Id, "l-0", 250m, 3),
            new(null, b.Id, "l-1", 300m, 2),
            new(null, b.Id, "l-2", 310m, 1)
        ]), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("MON-01", result.Value.Code);
        Assert.Equal(3, result.Value.Lots.Count);
        Assert.Contains(result.Value.Lots, s => s.LotNumber == "L-1");
    }

    [Fact]
    public async Task PriceMatrix_lists_every_supplier_even_without_lots_on_the_page()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        await _service.CreateAsync(new SaveProductRequest("P1", "Producto", null,
            [new(null, a.Id, "L1", 10m, 1)]), default);

        var matrix = await _service.GetPriceMatrixAsync(new ProductQuery(), default);

        // El proveedor B no tiene lotes en esta página y aun así es una columna.
        Assert.Equal([a.Id, b.Id], matrix.Suppliers.Select(s => s.Id).Order());
    }

    [Fact]
    public async Task Create_with_existing_code_returns_conflict()
    {
        await _service.CreateAsync(new SaveProductRequest("P1", "Producto", null, []), default);

        var result = await _service.CreateAsync(new SaveProductRequest("p1", "Otro", null, []), default);

        Assert.Equal(ProductService.DuplicateCode, result.Error);
    }

    [Fact]
    public async Task Create_with_repeated_supplier_and_lot_returns_validation_error()
    {
        var (a, _) = await _db.SeedSuppliersAsync();

        var result = await _service.CreateAsync(new SaveProductRequest("P1", "Producto", null,
            [new(null, a.Id, "L1", 10m, 1), new(null, a.Id, " l1 ", 12m, 1)]), default);

        Assert.Equal(ProductService.DuplicateLot, result.Error);
    }

    [Fact]
    public async Task Create_with_unknown_supplier_returns_validation_error()
    {
        var result = await _service.CreateAsync(new SaveProductRequest("P1", "Producto", null,
            [new(null, 999, "L1", 10m, 1)]), default);

        Assert.Equal(ProductService.UnknownSupplier, result.Error);
    }

    [Fact]
    public async Task Update_syncs_lots_updating_adding_and_removing()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        var created = (await _service.CreateAsync(new SaveProductRequest("P1", "Producto", null,
            [new(null, a.Id, "A1", 10m, 1), new(null, b.Id, "B1", 20m, 2)]), default)).Value;
        var lotA = created.Lots.Single(s => s.SupplierId == a.Id);

        var result = await _service.UpdateAsync(created.Id, new SaveProductRequest("P1", "Producto v2", null,
            [new(lotA.Id, a.Id, "A1", 15m, 5), new(null, b.Id, "NEW", 18m, 4)]), default);

        Assert.True(result.IsSuccess);
        await using var check = _db.CreateContext();
        var lots = await check.InventoryLots.Where(ps => ps.ProductId == created.Id).ToListAsync();
        Assert.Equal(2, lots.Count);
        Assert.Contains(lots, l => l.Id == lotA.Id && l.Price == 15m && l.Stock == 5);
        Assert.Contains(lots, l => l.SupplierId == b.Id && l.LotNumber == "NEW");
        Assert.DoesNotContain(lots, l => l.SupplierId == b.Id && l.LotNumber == "B1");
    }

    [Fact]
    public async Task Update_of_only_the_inventory_marks_the_product_as_updated()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var created = (await _service.CreateAsync(new SaveProductRequest("P1", "Uno", null, [new(null, a.Id, "L1", 1m, 1)]), default)).Value;
        var lotId = created.Lots[0].Id;

        // Same code, name and description: only the stock changes.
        var updated = await _service.UpdateAsync(created.Id, new SaveProductRequest("P1", "Uno", null,
            [new(lotId, a.Id, "L1", 1m, 50)]), default);

        Assert.True(updated.Value.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task Update_rejects_lot_ids_from_another_product()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var other = (await _service.CreateAsync(new SaveProductRequest("P1", "Uno", null, [new(null, a.Id, "L1", 1m, 1)]), default)).Value;
        var target = (await _service.CreateAsync(new SaveProductRequest("P2", "Dos", null, []), default)).Value;

        var result = await _service.UpdateAsync(target.Id, new SaveProductRequest("P2", "Dos", null,
            [new(other.Lots[0].Id, a.Id, "L1", 1m, 1)]), default);

        Assert.Equal(ProductService.UnknownLot, result.Error);
    }

    [Fact]
    public async Task Delete_hides_product_and_its_lots_but_keeps_the_rows()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var created = (await _service.CreateAsync(new SaveProductRequest("P1", "Uno", null, [new(null, a.Id, "L1", 1m, 1)]), default)).Value;

        var result = await _service.DeleteAsync(created.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductService.NotFound, (await _service.GetByIdAsync(created.Id, default)).Error);
        Assert.Equal(ProductService.NotFound, (await _service.DeleteAsync(created.Id, default)).Error);

        await using var check = _db.CreateContext();
        Assert.False(await check.InventoryLots.AnyAsync());
        var stored = await check.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id);
        Assert.NotNull(stored.DeletedAt);
        Assert.True(await check.InventoryLots.IgnoreQueryFilters().AnyAsync(lot => lot.ProductId == created.Id));
    }

    [Fact]
    public async Task Code_of_a_deleted_product_can_be_reused()
    {
        var created = (await _service.CreateAsync(new SaveProductRequest("P1", "Uno", null, []), default)).Value;
        await _service.DeleteAsync(created.Id, default);

        var result = await _service.CreateAsync(new SaveProductRequest("P1", "Otro", null, []), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task List_filters_by_search_and_supplier()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        await _service.CreateAsync(new SaveProductRequest("MON-1", "Monitor", null, [new(null, a.Id, "L1", 1m, 1)]), default);
        await _service.CreateAsync(new SaveProductRequest("TEC-1", "Teclado", null, [new(null, b.Id, "L1", 1m, 1)]), default);

        var bySearch = await _service.GetPagedAsync(new ProductQuery { Search = "Moni" } /* SQLite is case-sensitive; SQL Server CI collation is not */, default);
        var bySupplier = await _service.GetPagedAsync(new ProductQuery { SupplierId = b.Id }, default);

        Assert.Equal("MON-1", Assert.Single(bySearch.Items).Code);
        Assert.Equal("TEC-1", Assert.Single(bySupplier.Items).Code);
    }

    [Fact]
    public async Task Withdraw_subtracts_units_from_the_lot()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var created = (await _service.CreateAsync(new SaveProductRequest("P1", "Producto", null,
            [new(null, a.Id, "L1", 10m, 5)]), default)).Value;
        var lot = Assert.Single(created.Lots);

        var result = await _service.WithdrawStockAsync(created.Id, lot.Id, new WithdrawStockRequest(3), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, Assert.Single(result.Value.Lots).Stock);
    }

    [Fact]
    public async Task Withdraw_more_than_available_returns_conflict_and_keeps_stock()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var created = (await _service.CreateAsync(new SaveProductRequest("P1", "Producto", null,
            [new(null, a.Id, "L1", 10m, 2)]), default)).Value;
        var lot = Assert.Single(created.Lots);

        var result = await _service.WithdrawStockAsync(created.Id, lot.Id, new WithdrawStockRequest(3), default);

        Assert.Equal(ProductService.InsufficientStock, result.Error);
        var unchanged = (await _service.GetByIdAsync(created.Id, default)).Value;
        Assert.Equal(2, Assert.Single(unchanged.Lots).Stock);
    }

    [Fact]
    public async Task Withdraw_from_a_lot_of_another_product_returns_not_found()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var first = (await _service.CreateAsync(new SaveProductRequest("P1", "Uno", null,
            [new(null, a.Id, "L1", 10m, 5)]), default)).Value;
        var second = (await _service.CreateAsync(new SaveProductRequest("P2", "Dos", null, []), default)).Value;
        var lotOfFirst = Assert.Single(first.Lots);

        var result = await _service.WithdrawStockAsync(second.Id, lotOfFirst.Id, new WithdrawStockRequest(1), default);

        Assert.Equal(ProductService.LotNotFound, result.Error);
    }

    public void Dispose() => _db.Dispose();
}
