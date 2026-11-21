using System.ClientModel;
using System.ClientModel.Primitives;
using BgInventario.Core.Features.Invoices;
using BgInventario.Infrastructure.Invoices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace BgInventario.Tests;

/// <summary>Builds real PDFs in memory with PdfPig's writer, so the reader is tested on actual files.</summary>
internal static class TestPdf
{
    private const double FontSize = 10;

    /// <summary>A page with each text drawn at the given position (points from the bottom-left corner).</summary>
    public static byte[] WithText(params (string Text, double X, double Y)[] texts)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var (text, x, y) in texts)
        {
            page.AddText(text, FontSize, new PdfPoint(x, y), font);
        }

        return builder.Build();
    }

    /// <summary>A page without a text layer, like a scan.</summary>
    public static byte[] Blank() => BlankPages(1);

    /// <summary>Several pages without a text layer, like a scanned document.</summary>
    public static byte[] BlankPages(int count)
    {
        var builder = new PdfDocumentBuilder();
        for (var page = 0; page < count; page++)
        {
            builder.AddPage(PageSize.A4);
        }

        return builder.Build();
    }

    /// <summary>Pages full of text lines, to go over the text limit with few pages.</summary>
    public static byte[] TextPages(int pageCount, int linesPerPage, int lineLength)
    {
        const double lineHeight = 12;
        const double top = 800;
        const double left = 20;
        // Small font so a long line still fits in the width of the page.
        const double smallFontSize = 4;

        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var line = new string('x', lineLength);
        for (var pageNumber = 0; pageNumber < pageCount; pageNumber++)
        {
            var page = builder.AddPage(PageSize.A4);
            for (var lineNumber = 0; lineNumber < linesPerPage; lineNumber++)
            {
                page.AddText(line, smallFontSize, new PdfPoint(left, top - lineNumber * lineHeight), font);
            }
        }

        return builder.Build();
    }

    // A small invoice whose lines are drawn in reverse order inside the file, as some generators do.
    public static byte[] Invoice() => WithText(
        ("2", 50, 680), ("MON 24 FHD", 100, 680), ("150,00", 300, 680), ("300,00", 380, 680),
        ("CANT", 50, 700), ("DESCRIPCION", 100, 700), ("P.UNIT", 300, 700), ("TOTAL", 380, 700),
        ("FACTURA 001-001-000123", 50, 760));
}

public sealed class PdfPageReaderTests
{
    [Fact]
    public void Text_page_is_rebuilt_in_visual_order_with_aligned_columns()
    {
        var page = Assert.IsType<TextPage>(ReadPages(TestPdf.Invoice()).Value.Single());

        var lines = page.Text.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("FACTURA", lines[0]);
        Assert.StartsWith("CANT", lines[1]);
        // The header and the row share their columns, so the model can tell which number is which.
        Assert.Equal(lines[1].IndexOf("DESCRIPCION"), lines[2].IndexOf("MON"));
        Assert.Equal(lines[1].IndexOf("TOTAL"), lines[2].IndexOf("300,00"));
    }

    [Fact]
    public void Page_without_text_is_rendered_as_png()
    {
        var page = Assert.IsType<ScannedPage>(ReadPages(TestPdf.Blank()).Value.Single());

        Assert.Equal(InvoiceFileType.Png, InvoiceDocument.DetectType(page.Image));
    }

    [Fact]
    public void Pdf_with_too_many_pages_is_rejected_before_reading_them()
    {
        var result = ReadPages(TestPdf.BlankPages(PdfPageReader.MaxPages + 1));

        Assert.Equal(InvoiceErrors.DocumentTooLarge, result.Error);
    }

    [Fact]
    public void Pdf_with_more_scanned_pages_than_the_model_accepts_is_rejected()
    {
        // Within the page limit, but each page would be one more image than the model accepts.
        var result = ReadPages(TestPdf.BlankPages(LlmInvoiceExtractor.MaxImagesPerRequest + 1));

        Assert.Equal(InvoiceErrors.TooManyScannedPages, result.Error);
    }

    [Fact]
    public void Pdf_with_too_much_text_is_rejected()
    {
        // 15 pages × 60 lines × 120 characters ≈ 108,000, over the limit with fewer pages than the maximum.
        var result = ReadPages(TestPdf.TextPages(pageCount: 15, linesPerPage: 60, lineLength: 120));

        Assert.Equal(InvoiceErrors.DocumentTooLarge, result.Error);
    }

    private static BgInventario.Core.Common.Result<List<InvoicePage>> ReadPages(byte[] pdf) =>
        PdfPageReader.ReadPages(pdf, LlmInvoiceExtractor.MaxImagesPerRequest);
}

public sealed class PrintedNumbersTests
{
    [Fact]
    public void Reads_both_decimal_conventions()
    {
        var numbers = PrintedNumbers.Find("Total 1.234,56 - Subtotal 1,234.50 - Cant. 10");

        Assert.Contains(1234.56m, numbers);
        Assert.Contains(1234.5m, numbers);
        Assert.Contains(10m, numbers);
    }
}

public sealed class LlmInvoiceExtractorTests
{
    private static readonly InvoiceExtractionOptions Options = new() { ApiKey = "test" };

    private static LlmInvoiceExtractor CreateExtractor(FakeChatClient chat) =>
        new(chat, Options, NullLogger<LlmInvoiceExtractor>.Instance);

    [Fact]
    public async Task Text_pdf_is_sent_as_text_and_the_answer_is_mapped()
    {
        var chat = new FakeChatClient("""
            {"invoiceNumber":" 001-001-000123 ","issueDate":"2026-11-20","supplierName":"Proveedor A",
             "subtotal":300,"total":345,
             "lines":[{"description":" MON 24 FHD ","supplierItemCode":"","quantity":2,"unitPrice":150}]}
            """);
        var document = new InvoiceDocument(TestPdf.Invoice(), InvoiceFileType.Pdf);

        var result = await CreateExtractor(chat).ExtractAsync(document, default);

        Assert.True(result.IsSuccess);
        var invoice = result.Value;
        Assert.Equal("001-001-000123", invoice.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 11, 20), invoice.IssueDate);
        Assert.Equal(new ExtractedInvoiceLine("MON 24 FHD", null, 2, 150m), invoice.Lines.Single());
        Assert.Empty(invoice.Warnings);

        var sent = chat.LastMessages!.Last().Contents;
        Assert.Contains(sent.OfType<TextContent>(), c => c.Text.StartsWith("Página 1 (texto)"));
        Assert.Empty(sent.OfType<DataContent>());
    }

    [Fact]
    public async Task Numbers_missing_from_the_pdf_text_are_flagged()
    {
        var chat = new FakeChatClient("""
            {"invoiceNumber":"1","issueDate":null,"supplierName":null,"subtotal":null,"total":null,
             "lines":[{"description":"MON 24 FHD","supplierItemCode":null,"quantity":2,"unitPrice":155}]}
            """);
        var document = new InvoiceDocument(TestPdf.Invoice(), InvoiceFileType.Pdf);

        var result = await CreateExtractor(chat).ExtractAsync(document, default);

        Assert.Contains(result.Value.Warnings, w => w.Contains("155"));
    }

    [Fact]
    public async Task Scanned_pdf_page_is_sent_as_image()
    {
        var chat = new FakeChatClient("""
            {"invoiceNumber":null,"issueDate":null,"supplierName":null,"subtotal":null,"total":null,"lines":[]}
            """);
        var document = new InvoiceDocument(TestPdf.Blank(), InvoiceFileType.Pdf);

        var result = await CreateExtractor(chat).ExtractAsync(document, default);

        Assert.Single(chat.LastMessages!.Last().Contents.OfType<DataContent>());
        Assert.Contains(result.Value.Warnings, w => w.Contains("leídas como imagen"));
    }

    [Fact]
    public async Task Damaged_pdf_is_reported_as_unreadable()
    {
        var chat = new FakeChatClient("{}");
        var document = new InvoiceDocument("%PDF-1.7 not really a pdf"u8.ToArray(), InvoiceFileType.Pdf);

        var result = await CreateExtractor(chat).ExtractAsync(document, default);

        Assert.Equal(InvoiceErrors.UnreadableDocument, result.Error);
        Assert.Null(chat.LastMessages);
    }

    [Fact]
    public async Task Answer_outside_the_schema_is_uninterpretable()
    {
        var chat = new FakeChatClient("no es json");
        var document = new InvoiceDocument(TestPdf.Invoice(), InvoiceFileType.Pdf);

        var result = await CreateExtractor(chat).ExtractAsync(document, default);

        Assert.Equal(InvoiceErrors.UninterpretableResponse, result.Error);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)] // the rendered images are too large
    [InlineData(422)]
    public async Task Provider_rejection_of_the_input_is_not_reported_as_an_outage(int status)
    {
        var chat = new FakeChatClient("{}") { Failure = ProviderError(status) };
        var document = new InvoiceDocument(TestPdf.Invoice(), InvoiceFileType.Pdf);

        var result = await CreateExtractor(chat).ExtractAsync(document, default);

        Assert.Equal(InvoiceErrors.ExtractionRejected, result.Error);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Provider_errors_that_do_not_depend_on_the_file_are_an_outage(int status)
    {
        var chat = new FakeChatClient("{}") { Failure = ProviderError(status) };
        var document = new InvoiceDocument(TestPdf.Invoice(), InvoiceFileType.Pdf);

        var result = await CreateExtractor(chat).ExtractAsync(document, default);

        Assert.Equal(InvoiceErrors.ExtractionFailed, result.Error);
    }

    private static ClientResultException ProviderError(int status) =>
        new("provider error", new FakeResponse(status));

    /// <summary>
    /// Answers every request with a fixed text and records it; with <see cref="Failure"/> it
    /// throws like the provider's SDK on an HTTP error.
    /// </summary>
    private sealed class FakeChatClient(string answer) : IChatClient
    {
        public List<ChatMessage>? LastMessages { get; private set; }
        public Exception? Failure { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>HTTP answer with only a status code: what ClientResultException reads its Status from.</summary>
    private sealed class FakeResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.Empty;
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);

        public override void Dispose()
        {
        }
    }
}
