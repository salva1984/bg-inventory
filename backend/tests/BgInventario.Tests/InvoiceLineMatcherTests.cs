using BgInventario.Core.Entities;
using BgInventario.Core.Features.Invoices;
using BgInventario.Core.Features.Products;

namespace BgInventario.Tests;

public sealed class ProductSearchIndexTests
{
    [Fact]
    public void Normalize_removes_accents_case_and_symbols()
    {
        Assert.Equal("toner hp ce285a negro", ProductSearchIndex.Normalize("  Tóner HP-CE285A (NEGRO) "));
    }

    [Fact]
    public void GetTrigrams_pads_each_word_like_pg_trgm()
    {
        var trigrams = ProductSearchIndex.GetTrigrams("24");

        Assert.Equal(["  2", " 24", "24 "], trigrams.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Refresh_only_changes_the_difference()
    {
        var product = new Product { Code = "P1", Name = "Mouse" };
        ProductSearchIndex.Refresh(product);
        var kept = product.Trigrams.Single(t => t.Trigram == " mo");

        product.Name = "Monitor";
        ProductSearchIndex.Refresh(product);

        // The shared trigram keeps its row instead of being removed and added again.
        Assert.Contains(kept, product.Trigrams);
        Assert.DoesNotContain(product.Trigrams, t => t.Trigram == "use");
        Assert.Contains(product.Trigrams, t => t.Trigram == "tor");
    }
}

public sealed class InvoiceLineMatcherTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly InvoiceLineMatcher _matcher;
    private readonly ProductService _products;

    public InvoiceLineMatcherTests()
    {
        _matcher = new InvoiceLineMatcher(_db.Context);
        _products = new ProductService(_db.Context, TimeProvider.System, _db.DashboardCache);
    }

    /// <summary>Small catalog created through ProductService, so the trigrams are built as in production.</summary>
    private async Task<Dictionary<string, int>> SeedCatalogAsync()
    {
        (string Code, string Name)[] catalog =
        [
            ("MON-SAM-24", "Monitor Samsung 24\" Full HD"),
            ("MON-LG-27", "Monitor LG 27\" 4K"),
            ("CAB-HDMI-2M", "Cable HDMI Ugreen 2 metros"),
            ("TON-HP-85A", "Tóner HP CE285A"),
            ("MOU-INAL", "Mouse inalámbrico")
        ];

        var ids = new Dictionary<string, int>();
        foreach (var (code, name) in catalog)
        {
            var created = await _products.CreateAsync(new SaveProductRequest(code, name, null, []), default);
            ids[code] = created.Value.Id;
        }

        return ids;
    }

    private static ExtractedInvoiceLine Line(string description, string? itemCode = null) =>
        new(description, itemCode, 1, 10m);

    private async Task<LineMatch> MatchOneAsync(int supplierId, ExtractedInvoiceLine line) =>
        (await _matcher.MatchAsync(supplierId, [line], default)).Single();

    [Theory]
    [InlineData("CABLE HDMI 2M", "CAB-HDMI-2M")]
    [InlineData("TONER HP 85A", "TON-HP-85A")]
    [InlineData("MOUSE INALAMBRICO", "MOU-INAL")]
    public async Task Search_ranks_the_right_product_first(string description, string expectedCode)
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await SeedCatalogAsync();

        var match = await MatchOneAsync(a.Id, Line(description));

        Assert.NotEqual(LineMatchStatus.Manual, match.Status);
        Assert.Equal(expectedCode, match.Candidates[0].Code);
    }

    [Fact]
    public async Task Search_suggests_when_the_description_is_close()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await SeedCatalogAsync();

        var match = await MatchOneAsync(a.Id, Line("Mouse inalambrico"));

        Assert.Equal(LineMatchStatus.Suggested, match.Status);
        Assert.True(match.Candidates[0].Score >= InvoiceLineMatcher.SuggestedMinScore);
    }

    [Theory]
    [InlineData("ARTICULO VARIOS")] // nothing in the catalog resembles it
    [InlineData("AB")]              // too short to search
    public async Task Poor_descriptions_require_manual_identification(string description)
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await SeedCatalogAsync();

        var match = await MatchOneAsync(a.Id, Line(description));

        Assert.Equal(LineMatchStatus.Manual, match.Status);
        Assert.Empty(match.Candidates);
    }

    [Fact]
    public async Task Alias_matches_only_for_the_supplier_that_confirmed_it()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        var ids = await SeedCatalogAsync();
        _db.Context.SupplierProductAliases.Add(new SupplierProductAlias
        {
            SupplierId = a.Id,
            ProductId = ids["MON-SAM-24"],
            Description = "MON 24 FHD",
            NormalizedDescription = ProductSearchIndex.Normalize("MON 24 FHD")
        });
        await _db.Context.SaveChangesAsync();

        var fromA = await MatchOneAsync(a.Id, Line("mon 24 fhd"));
        var fromB = await MatchOneAsync(b.Id, Line("mon 24 fhd"));

        Assert.Equal(LineMatchStatus.Matched, fromA.Status);
        Assert.Equal(MatchedBy.Description, fromA.MatchedBy);
        Assert.Equal(ids["MON-SAM-24"], fromA.Candidates.Single().ProductId);
        Assert.NotEqual(LineMatchStatus.Matched, fromB.Status);
    }

    [Fact]
    public async Task Item_code_equal_to_our_product_code_matches()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var ids = await SeedCatalogAsync();

        var match = await MatchOneAsync(a.Id, Line("ARTICULO 001", itemCode: " mou-inal "));

        Assert.Equal(LineMatchStatus.Matched, match.Status);
        Assert.Equal(MatchedBy.ProductCode, match.MatchedBy);
        Assert.Equal(ids["MOU-INAL"], match.Candidates.Single().ProductId);
    }

    [Fact]
    public async Task Supplier_item_code_used_for_two_products_is_not_trusted()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var ids = await SeedCatalogAsync();
        _db.Context.SupplierProductAliases.AddRange(
            Alias(a.Id, ids["MON-SAM-24"], "MONITOR 24", "X-100"),
            Alias(a.Id, ids["MON-LG-27"], "MONITOR 27", "X-100"));
        await _db.Context.SaveChangesAsync();

        var match = await MatchOneAsync(a.Id, Line("ARTICULO VARIOS", itemCode: "X-100"));

        Assert.Equal(LineMatchStatus.Manual, match.Status);
    }

    [Fact]
    public async Task Deleted_products_are_not_candidates()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var ids = await SeedCatalogAsync();
        await _products.DeleteAsync(ids["MOU-INAL"], default);

        var match = await MatchOneAsync(a.Id, Line("Mouse inalambrico"));

        Assert.DoesNotContain(match.Candidates, c => c.ProductId == ids["MOU-INAL"]);
    }

    private static SupplierProductAlias Alias(int supplierId, int productId, string description, string itemCode) => new()
    {
        SupplierId = supplierId,
        ProductId = productId,
        Description = description,
        NormalizedDescription = ProductSearchIndex.Normalize(description),
        SupplierItemCode = itemCode
    };

    public void Dispose() => _db.Dispose();
}
