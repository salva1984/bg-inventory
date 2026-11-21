using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Invoices;
using BgInventario.Core.Features.Products;
using BgInventario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BgInventario.Tests;

public sealed class InvoiceServiceTests : IDisposable
{
    // Smallest content the service accepts as a PDF: only the signature is checked before extraction.
    private static readonly byte[] PdfFile = "%PDF-1.7 fake"u8.ToArray();

    private readonly TestDb _db = new();
    private readonly FakeInvoiceExtractor _extractor = new();
    private readonly InvoiceService _service;
    private readonly ProductService _products;
    private readonly InvoiceReceiptService _receipts;

    // The admin who confirms the invoices: the history records who registered each one.
    private readonly User _admin = new()
    {
        Email = "admin@inventario.local",
        FullName = "Ana Admin",
        PasswordHash = "not-used",
        Role = UserRole.Admin
    };

    public InvoiceServiceTests()
    {
        var matcher = new InvoiceLineMatcher(_db.Context);
        _service = new InvoiceService(_db.Context, _extractor, matcher, TimeProvider.System, _db.DashboardCache);
        _products = new ProductService(_db.Context, TimeProvider.System, _db.DashboardCache);
        _receipts = new InvoiceReceiptService(_db.Context);

        _db.Context.Users.Add(_admin);
        _db.Context.SaveChanges();
    }

    /// <summary>A confirmation with the analyzed PDF attached, as the review screen sends it.</summary>
    private static ConfirmInvoiceRequest Request(
        int supplierId, string invoiceNumber, IReadOnlyList<ConfirmInvoiceLineRequest> lines) =>
        new(supplierId, invoiceNumber, IssueDate: null, InvoiceTotal: null, new InvoiceFileUpload("factura.pdf", PdfFile), lines);

    private Task<Result<ConfirmedInvoice>> ConfirmAsync(ConfirmInvoiceRequest request) =>
        _service.ConfirmAsync(request, _admin.Id, default);

    private async Task<int> CreateProductAsync(string code, string name) =>
        (await _products.CreateAsync(new SaveProductRequest(code, name, null, []), default)).Value.Id;

    private static ExtractedInvoice Invoice(decimal? subtotal, params ExtractedInvoiceLine[] lines) =>
        new("001-001-000123", new DateOnly(2026, 11, 20), "Proveedor A", subtotal, null, lines, []);

    [Fact]
    public async Task Analyze_rejects_files_that_are_not_pdf_or_images()
    {
        var (a, _) = await _db.SeedSuppliersAsync();

        var result = await _service.AnalyzeAsync(a.Id, "hola"u8.ToArray(), default);

        Assert.Equal(InvoiceErrors.UnsupportedFile, result.Error);
    }

    [Fact]
    public async Task Analyze_rejects_unknown_supplier()
    {
        var result = await _service.AnalyzeAsync(999, PdfFile, default);

        Assert.Equal(InvoiceErrors.UnknownSupplier, result.Error);
    }

    [Fact]
    public async Task Analyze_matches_lines_and_warns_when_the_subtotal_does_not_add_up()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var cableId = await CreateProductAsync("CAB-HDMI-2M", "Cable HDMI Ugreen 2 metros");
        _extractor.Next = Invoice(subtotal: 100m,
            new ExtractedInvoiceLine("CABLE HDMI 2M", null, 10, 4.50m),
            new ExtractedInvoiceLine("ARTICULO VARIOS", null, 1, 20m));

        var result = await _service.AnalyzeAsync(a.Id, PdfFile, default);

        Assert.True(result.IsSuccess);
        var analysis = result.Value;
        Assert.Equal(cableId, analysis.Lines[0].Match.Candidates[0].ProductId);
        Assert.Equal(LineMatchStatus.Manual, analysis.Lines[1].Match.Status);
        // 10 × 4.50 + 20 = 65, but the invoice says 100.
        Assert.Contains(analysis.Warnings, w => w.Contains("subtotal"));
    }

    [Theory]
    // The invoice writes the name in capitals and without accents: still the chosen supplier.
    [InlineData("PROVEEDOR A", IssuerCheck.Matches, null)]
    // "Proveedor A" and "Proveedor B" share most trigrams; the most similar one decides.
    [InlineData("Proveedor B", IssuerCheck.OtherSupplier, "Proveedor B")]
    [InlineData("Shenzhen Global Electronics Co., Ltd.", IssuerCheck.NotRegistered, null)]
    [InlineData(null, IssuerCheck.NotRead, null)]
    public async Task Analyze_reports_whether_the_issuer_is_the_chosen_supplier(
        string? issuer, IssuerCheck expected, string? expectedSuggestion)
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        _extractor.Next = Invoice(subtotal: null, new ExtractedInvoiceLine("MOUSE", null, 1, 10m)) with
        {
            SupplierName = issuer
        };

        var analysis = (await _service.AnalyzeAsync(a.Id, PdfFile, default)).Value;

        // The choice is never overwritten: the lines stay matched for the chosen supplier.
        Assert.Equal(a.Id, analysis.SupplierId);
        Assert.Equal(expected, analysis.IssuerCheck);
        Assert.Equal(expectedSuggestion, analysis.SuggestedSupplier?.Name);
    }

    [Fact]
    public async Task Analyze_without_lines_is_an_error()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        _extractor.Next = Invoice(subtotal: null);

        var result = await _service.AnalyzeAsync(a.Id, PdfFile, default);

        Assert.Equal(InvoiceErrors.NoLines, result.Error);
    }

    [Fact]
    public async Task Analyze_returns_the_extractor_error()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        _extractor.Error = InvoiceErrors.ExtractionFailed;

        var result = await _service.AnalyzeAsync(a.Id, PdfFile, default);

        Assert.Equal(InvoiceErrors.ExtractionFailed, result.Error);
    }

    [Fact]
    public async Task Confirm_registers_stock_as_a_lot_named_after_the_invoice()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var monitorId = await CreateProductAsync("MON-SAM-24", "Monitor Samsung 24\" Full HD");

        var result = await ConfirmAsync(Request(a.Id, " 001-001-000123 ",
            [new ConfirmInvoiceLineRequest(monitorId, "MON 24 FHD", "sam-24", 3, 150m)]));

        Assert.True(result.IsSuccess);
        Assert.Equal(new ConfirmedInvoice(result.Value.ReceiptId, "001-001-000123", 1, 3, 1), result.Value);

        await using var check = _db.CreateContext();
        var lot = await check.InventoryLots.SingleAsync();
        Assert.Equal((monitorId, a.Id, "001-001-000123", 150m, 3), (lot.ProductId, lot.SupplierId, lot.LotNumber, lot.Price, lot.Stock));
        var alias = await check.SupplierProductAliases.SingleAsync();
        Assert.Equal(("MON 24 FHD", "mon 24 fhd", "SAM-24"), (alias.Description, alias.NormalizedDescription, alias.SupplierItemCode));
    }

    [Fact]
    public async Task Confirm_merges_repeated_products_with_the_weighted_average_price()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var cableId = await CreateProductAsync("CAB-HDMI-2M", "Cable HDMI Ugreen 2 metros");

        var result = await ConfirmAsync(Request(a.Id, "F-1",
        [
            new ConfirmInvoiceLineRequest(cableId, "CABLE HDMI 2M", null, 10, 4m),
            new ConfirmInvoiceLineRequest(cableId, "CABLE HDMI 2M PROMO", null, 30, 2m)
        ]));

        Assert.True(result.IsSuccess);
        await using var check = _db.CreateContext();
        var lot = await check.InventoryLots.SingleAsync();
        Assert.Equal(40, lot.Stock);
        Assert.Equal(2.5m, lot.Price); // (10×4 + 30×2) / 40
    }

    [Fact]
    public async Task Confirm_the_same_invoice_twice_is_a_conflict()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        var request = Request(a.Id, "F-1", [new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 1, 10m)]);
        await ConfirmAsync(request);

        var second = await ConfirmAsync(request with { InvoiceNumber = "f-1" });

        Assert.Equal(InvoiceErrors.AlreadyRegistered, second.Error);
    }

    [Fact]
    public async Task Confirm_detects_a_registered_invoice_whose_products_were_deleted()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        var keyboardId = await CreateProductAsync("TEC-USB", "Teclado USB");
        await ConfirmAsync(Request(a.Id, "F-1", [new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 1, 10m)]));
        // The soft delete hides the invoice's only lot; the invoice is still registered.
        await _products.DeleteAsync(mouseId, default);

        var second = await ConfirmAsync(Request(a.Id, "F-1", [new ConfirmInvoiceLineRequest(keyboardId, "TECLADO", null, 1, 10m)]));

        Assert.Equal(InvoiceErrors.AlreadyRegistered, second.Error);
    }

    [Fact]
    public async Task Registering_an_invoice_again_adds_its_stock_to_the_same_lot_and_keeps_both_records()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        await ConfirmAsync(Request(a.Id, "F-1", [new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 2, 10m)]));

        var again = await ConfirmAsync(Request(a.Id, "F-1",
            [new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 3, 20m)]) with { RegisterAgain = true });

        Assert.True(again.IsSuccess);
        Assert.Equal((1, 3), (again.Value.ProductsReceived, again.Value.UnitsReceived));
        await using var check = _db.CreateContext();
        var lot = await check.InventoryLots.SingleAsync();
        Assert.Equal(5, lot.Stock);
        Assert.Equal(16m, lot.Price); // (2×10 + 3×20) / 5
        Assert.Equal(2, await check.InvoiceReceipts.CountAsync());
    }

    [Fact]
    public async Task Confirm_with_unknown_product_saves_nothing()
    {
        var (a, _) = await _db.SeedSuppliersAsync();

        var result = await ConfirmAsync(Request(a.Id, "F-1",
            [new ConfirmInvoiceLineRequest(999, "ALGO", null, 1, 10m)]));

        Assert.Equal(InvoiceErrors.UnknownProduct, result.Error);
        await using var check = _db.CreateContext();
        Assert.False(await check.InventoryLots.AnyAsync());
        Assert.False(await check.InvoiceReceipts.AnyAsync());
    }

    [Fact]
    public async Task Confirm_records_the_invoice_with_its_file_and_what_each_line_was_mapped_to()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var monitorId = await CreateProductAsync("MON-SAM-24", "Monitor Samsung 24\" Full HD");
        var cableId = await CreateProductAsync("CAB-HDMI-2M", "Cable HDMI 2 metros");
        var request = Request(a.Id, " 001-001-000123 ",
        [
            new ConfirmInvoiceLineRequest(monitorId, " MON 24 FHD ", " sam-24 ", 3, 150m),
            new ConfirmInvoiceLineRequest(cableId, "CABLE HDMI 2M", null, 10, 4.5m, ProductCreated: true)
        ]) with { IssueDate = new DateOnly(2026, 11, 20), InvoiceTotal = 569.25m };

        var confirmed = (await ConfirmAsync(request)).Value;
        var detail = (await _receipts.GetByIdAsync(confirmed.ReceiptId, default)).Value;

        Assert.Equal(("001-001-000123", "Proveedor A", "Ana Admin", "factura.pdf"),
            (detail.InvoiceNumber, detail.SupplierName, detail.RegisteredBy, detail.FileName));
        Assert.Equal(new DateOnly(2026, 11, 20), detail.IssueDate);
        Assert.Equal(495m, detail.Subtotal); // 3 × 150 + 10 × 4.50
        Assert.Equal(569.25m, detail.InvoiceTotal); // as read from the invoice, with 15 % tax
        Assert.Equal(
        [
            new InvoiceReceiptLineItem(1, "MON 24 FHD", "sam-24", monitorId, "MON-SAM-24", "Monitor Samsung 24\" Full HD", false, 3, 150m),
            new InvoiceReceiptLineItem(2, "CABLE HDMI 2M", null, cableId, "CAB-HDMI-2M", "Cable HDMI 2 metros", true, 10, 4.5m)
        ], detail.Lines);

        var file = (await _receipts.GetFileAsync(confirmed.ReceiptId, default)).Value;
        Assert.Equal(("factura.pdf", "application/pdf"), (file.FileName, file.ContentType));
        Assert.Equal(PdfFile, file.Content);
    }

    [Fact]
    public async Task History_keeps_the_product_name_after_the_product_is_deleted()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        var confirmed = (await ConfirmAsync(Request(a.Id, "F-1",
            [new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 1, 10m)]))).Value;

        await _products.DeleteAsync(mouseId, default);

        var history = await _receipts.GetPagedAsync(new InvoiceReceiptQuery(), default);
        Assert.Equal(confirmed.ReceiptId, history.Items.Single().Id);
        var line = (await _receipts.GetByIdAsync(confirmed.ReceiptId, default)).Value.Lines.Single();
        Assert.Equal(("MOU-INAL", "Mouse inalámbrico"), (line.ProductCode, line.ProductName));
    }

    [Fact]
    public async Task History_lists_the_newest_invoice_first_and_searches_by_number_or_supplier()
    {
        var (a, b) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        var line = new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 1, 10m);
        await ConfirmAsync(Request(a.Id, "F-100", [line]));
        await ConfirmAsync(Request(b.Id, "F-200", [line]));

        var all = await _receipts.GetPagedAsync(new InvoiceReceiptQuery(), default);
        var byNumber = await _receipts.GetPagedAsync(new InvoiceReceiptQuery { Search = "100" }, default);
        var bySupplier = await _receipts.GetPagedAsync(new InvoiceReceiptQuery { Search = "Proveedor B" }, default);

        Assert.Equal(["F-200", "F-100"], all.Items.Select(item => item.InvoiceNumber));
        Assert.Equal("F-100", byNumber.Items.Single().InvoiceNumber);
        Assert.Equal("F-200", bySupplier.Items.Single().InvoiceNumber);
    }

    [Fact]
    public async Task History_of_an_unknown_invoice_is_not_found()
    {
        var detail = await _receipts.GetByIdAsync(999, default);
        var file = await _receipts.GetFileAsync(999, default);

        Assert.Equal(InvoiceErrors.ReceiptNotFound, detail.Error);
        Assert.Equal(InvoiceErrors.ReceiptNotFound, file.Error);
    }

    [Fact]
    public async Task Confirm_rejects_a_file_that_is_not_an_invoice_and_saves_nothing()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        var request = Request(a.Id, "F-1", [new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 1, 10m)]) with
        {
            File = new InvoiceFileUpload("notas.txt", "hola"u8.ToArray())
        };

        var result = await ConfirmAsync(request);

        Assert.Equal(InvoiceErrors.UnsupportedFile, result.Error);
        await using var check = _db.CreateContext();
        Assert.False(await check.InventoryLots.AnyAsync());
        Assert.False(await check.InvoiceReceipts.AnyAsync());
    }

    [Fact]
    public async Task Next_invoice_recognizes_what_the_user_confirmed()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var monitorId = await CreateProductAsync("MON-SAM-24", "Monitor Samsung 24\" Full HD");
        await ConfirmAsync(Request(a.Id, "F-1",
            [new ConfirmInvoiceLineRequest(monitorId, "PANTALLA 24 PULG", null, 2, 150m)]));

        _extractor.Next = Invoice(subtotal: null, new ExtractedInvoiceLine("Pantalla 24 pulg.", null, 5, 150m)) with
        {
            InvoiceNumber = "F-2"
        };
        var analysis = (await _service.AnalyzeAsync(a.Id, PdfFile, default)).Value;

        var match = analysis.Lines.Single().Match;
        Assert.Equal(LineMatchStatus.Matched, match.Status);
        Assert.Equal(MatchedBy.Description, match.MatchedBy);
        Assert.Equal(monitorId, match.Candidates.Single().ProductId);
    }

    [Fact]
    public async Task Analyze_warns_when_the_invoice_was_already_registered()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        await ConfirmAsync(Request(a.Id, "001-001-000123",
            [new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 1, 10m)]));
        _extractor.Next = Invoice(subtotal: null, new ExtractedInvoiceLine("MOUSE", null, 1, 10m));

        var analysis = (await _service.AnalyzeAsync(a.Id, PdfFile, default)).Value;

        Assert.Contains(analysis.Warnings, w => w.Contains("ya fue registrada"));
    }

    [Fact]
    public async Task Confirm_reuses_the_alias_of_a_product_deleted_later()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var oldMonitorId = await CreateProductAsync("MON-OLD", "Monitor viejo");
        var newMonitorId = await CreateProductAsync("MON-NEW", "Monitor nuevo");
        await ConfirmAsync(Request(a.Id, "F-1",
            [new ConfirmInvoiceLineRequest(oldMonitorId, "MON 24 FHD", null, 1, 100m)]));
        await _products.DeleteAsync(oldMonitorId, default);

        // Same description: the alias hidden by the soft delete holds the unique key, so it is updated.
        var result = await ConfirmAsync(Request(a.Id, "F-2",
            [new ConfirmInvoiceLineRequest(newMonitorId, "mon 24 fhd", null, 1, 100m)]));

        Assert.True(result.IsSuccess);
        await using var check = _db.CreateContext();
        var alias = await check.SupplierProductAliases.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(newMonitorId, alias.ProductId);
    }

    [Fact]
    public async Task Confirm_rejects_quantities_whose_total_does_not_fit_in_the_stock()
    {
        var (a, _) = await _db.SeedSuppliersAsync();
        var mouseId = await CreateProductAsync("MOU-INAL", "Mouse inalámbrico");
        // Each line is valid on its own (up to int.MaxValue); together they overflow an int.
        var line = new ConfirmInvoiceLineRequest(mouseId, "MOUSE", null, 2_000_000_000, 10m);

        var result = await ConfirmAsync(Request(a.Id, "F-1", [line, line]));

        Assert.Equal(InvoiceErrors.QuantityTooLarge, result.Error);
        await using var check = _db.CreateContext();
        Assert.False(await check.InventoryLots.AnyAsync());
    }

    [Fact]
    public void ReviewAmounts_warns_about_huge_amounts_instead_of_overflowing()
    {
        // Values the model could answer: their product overflows a decimal.
        var invoice = Invoice(subtotal: 1m, new ExtractedInvoiceLine("A", null, 1e15m, 1e15m));

        var warnings = InvoiceService.ReviewAmounts(invoice);

        Assert.Contains(warnings, w => w.StartsWith("Línea 1") && w.Contains("cantidad") && w.Contains("máximo"));
        Assert.Contains(warnings, w => w.StartsWith("Línea 1") && w.Contains("precio") && w.Contains("máximo"));
    }

    [Fact]
    public void Keys_computed_in_csharp_use_a_binary_collation()
    {
        // The SQL Server default collation equates values C# keeps apart (full-width "ＨＰ" and "hp")
        // and breaks unique keys. The collation is schema metadata: only the design-time model keeps it.
        var model = _db.Context.GetService<IDesignTimeModel>().Model;
        var trigram = model.FindEntityType(typeof(ProductTrigram))!.FindProperty(nameof(ProductTrigram.Trigram))!;
        var aliasKey = model.FindEntityType(typeof(SupplierProductAlias))!
            .FindProperty(nameof(SupplierProductAlias.NormalizedDescription))!;

        Assert.Equal(Collations.Ordinal, trigram.GetCollation());
        Assert.Equal(Collations.Ordinal, aliasKey.GetCollation());
    }

    [Fact]
    public void ReviewAmounts_flags_missing_number_and_impossible_amounts()
    {
        var invoice = Invoice(subtotal: null,
            new ExtractedInvoiceLine("A", null, 1.5m, 10m),
            new ExtractedInvoiceLine("B", null, 0, 0)) with { InvoiceNumber = null };

        var warnings = InvoiceService.ReviewAmounts(invoice);

        Assert.Contains(warnings, w => w.Contains("número de factura"));
        Assert.Contains(warnings, w => w.StartsWith("Línea 1") && w.Contains("entero"));
        Assert.Contains(warnings, w => w.StartsWith("Línea 2") && w.Contains("cantidad"));
        Assert.Contains(warnings, w => w.StartsWith("Línea 2") && w.Contains("precio"));
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, InvoiceFileType.Pdf)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, InvoiceFileType.Png)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, InvoiceFileType.Jpeg)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, InvoiceFileType.Webp)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, null)] // RIFF audio (WAV)
    public void DetectType_reads_the_file_signature(byte[] content, InvoiceFileType? expected)
    {
        Assert.Equal(expected, InvoiceDocument.DetectType(content));
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Returns a fixed invoice: the tests check our logic, not the model.</summary>
    private sealed class FakeInvoiceExtractor : IInvoiceExtractor
    {
        public ExtractedInvoice? Next { get; set; }
        public ErrorDetail? Error { get; set; }

        public Task<Result<ExtractedInvoice>> ExtractAsync(InvoiceDocument document, CancellationToken ct)
        {
            Result<ExtractedInvoice> result = Error is not null ? Error : Next!;
            return Task.FromResult(result);
        }
    }
}
