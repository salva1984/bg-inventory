using BgInventario.Core.Features.Assistant;
using BgInventario.Core.Features.Products;

namespace BgInventario.Tests;

public sealed class InventoryToolsTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ProductService _products;
    private readonly InventoryTools _tools;

    public InventoryToolsTests()
    {
        _products = new ProductService(_db.Context, TimeProvider.System, _db.DashboardCache);
        _tools = new InventoryTools(_db.Context);
    }

    [Fact]
    public async Task Search_matches_every_word_in_any_order_and_sums_the_lots()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("C1", "Cable de video HDMI", [new(null, a.Id, "L1", 8m, 3), new(null, b.Id, "L2", 6m, 4)]);
        await CreateProductAsync("C2", "Cable USB", [new(null, a.Id, "L3", 5m, 1)]);

        // Same case as the name: SQLite is case-sensitive; SQL Server's CI collation is not.
        var found = await _tools.SearchProductsAsync("HDMI Cable", default);

        var product = Assert.Single(found);
        Assert.Equal("C1", product.Code);
        Assert.Equal(7, product.TotalStock);
        Assert.Equal(2, product.SupplierCount);
        Assert.Equal(6m, product.MinPrice);
        Assert.Equal(8m, product.MaxPrice);
    }

    [Fact]
    public async Task Search_without_words_finds_nothing()
    {
        await CreateProductAsync("P1", "Monitor", []);

        var found = await _tools.SearchProductsAsync("   ", default);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Low_stock_lists_products_up_to_the_threshold_from_the_lowest()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("P1", "Monitor", [new(null, a.Id, "L1", 10m, 5)]);
        await CreateProductAsync("P2", "Parlante", [new(null, a.Id, "L2", 10m, 50)]);
        await CreateProductAsync("P3", "Teclado", []);

        var lowStock = await _tools.GetLowStockProductsAsync(threshold: 10, default);

        // Teclado has no lots: it counts as 0 units and comes first.
        Assert.Equal(["P3", "P1"], lowStock.Select(p => p.Code));
    }

    [Fact]
    public async Task Price_comparison_lists_each_supplier_once_from_the_cheapest()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("P1", "Monitor",
            [new(null, a.Id, "L1", 300m, 2), new(null, a.Id, "L2", 280m, 1), new(null, b.Id, "L3", 250m, 4)]);

        // The code is matched case-insensitively, as the model may write it either way.
        var comparison = await _tools.CompareSupplierPricesAsync("p1", default);

        Assert.NotNull(comparison);
        Assert.Equal(
            [new SupplierOffer(b.Name, 250m, 4), new SupplierOffer(a.Name, 280m, 3)],
            comparison.Offers);
    }

    [Fact]
    public async Task Price_comparison_of_an_unknown_code_is_null()
    {
        var comparison = await _tools.CompareSupplierPricesAsync("NO-EXISTE", default);

        Assert.Null(comparison);
    }

    private async Task CreateProductAsync(string code, string name, SaveInventoryLotRequest[] lots) =>
        await _products.CreateAsync(new SaveProductRequest(code, name, null, lots), default);

    public void Dispose() => _db.Dispose();
}
