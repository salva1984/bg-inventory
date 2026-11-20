using BgInventario.Core.Features.Products;
using BgInventario.Core.Features.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Tests;

/// <summary>Supplier deletion rules under soft delete.</summary>
public sealed class SupplierServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly SupplierService _service;
    private readonly ProductService _products;

    public SupplierServiceTests()
    {
        _service = new SupplierService(_db.Context, TimeProvider.System, _db.DashboardCache);
        _products = new ProductService(_db.Context, TimeProvider.System, _db.DashboardCache);
    }

    [Fact]
    public async Task Delete_hides_supplier_but_keeps_the_row()
    {
        var (a, _) = await _db.SeedSuppliersAsync();

        var result = await _service.DeleteAsync(a.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SupplierService.NotFound, (await _service.GetByIdAsync(a.Id, default)).Error);
        Assert.DoesNotContain(await _service.GetLookupAsync(default), s => s.Id == a.Id);

        await using var check = _db.CreateContext();
        var stored = await check.Suppliers.IgnoreQueryFilters().SingleAsync(s => s.Id == a.Id);
        Assert.NotNull(stored.DeletedAt);
    }

    [Fact]
    public async Task Delete_with_active_inventory_returns_conflict()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await _products.CreateAsync(new SaveProductRequest("P1", "Uno", null, [new(null, a.Id, "L1", 1m, 1)]), default);

        var result = await _service.DeleteAsync(a.Id, default);

        Assert.Equal(SupplierService.InUse, result.Error);
    }

    [Fact]
    public async Task Delete_is_allowed_when_its_only_products_were_deleted()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var product = (await _products.CreateAsync(new SaveProductRequest("P1", "Uno", null, [new(null, a.Id, "L1", 1m, 1)]), default)).Value;
        await _products.DeleteAsync(product.Id, default);

        var result = await _service.DeleteAsync(a.Id, default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Name_of_a_deleted_supplier_can_be_reused()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await _service.DeleteAsync(a.Id, default);

        var result = await _service.CreateAsync(new SaveSupplierRequest(a.Name, null, null), default);

        Assert.True(result.IsSuccess);
    }

    public void Dispose() => _db.Dispose();
}
