// Invoice extraction with an LLM through Microsoft.Extensions.AI (IChatClient). The model only
// turns the document into JSON; matching products and touching the stock is deterministic code in Core.
using System.ClientModel;
using System.Globalization;
using BgInventario.Core.Common;
using BgInventario.Core.Features.Invoices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BgInventario.Infrastructure.Invoices;

/// <summary>"InvoiceExtraction" configuration section. Any OpenAI-compatible endpoint works (OpenRouter by default).</summary>
public sealed class InvoiceExtractionOptions
{
    public const string SectionName = "InvoiceExtraction";

    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1";
    public string Model { get; init; } = "openai/gpt-6-luna";

    /// <summary>Secret; comes from an environment variable. Without it the analysis answers 503.</summary>
    public string? ApiKey { get; init; }

    public ReasoningEffort ReasoningEffort { get; init; } = ReasoningEffort.Medium;

    /// <summary>Reasoning over several pages can take a while; the SDK default (100 s) is too short.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Sends the invoice to the model, page by page in the form it reads best (see <see cref="PdfPageReader"/>),
/// and asks for a JSON with a fixed schema (structured output).
/// </summary>
internal sealed class LlmInvoiceExtractor(
    IChatClient chatClient, InvoiceExtractionOptions options, ILogger<LlmInvoiceExtractor> logger) : IInvoiceExtractor
{
    // Most vision models accept at most this many images per request (OpenAI documents 10).
    internal const int MaxImagesPerRequest = 10;

    private const string Instructions = """
        Eres un sistema que extrae datos de facturas de proveedores para registrar mercadería en un inventario.
        Recibirás la factura como imágenes o como texto. El texto se reconstruyó a partir de la posición de
        cada palabra en la página, así que las columnas de las tablas están alineadas con espacios.

        Reglas:
        - Extrae solo lo que aparece en el documento. Si un dato no está, devuelve null. Nunca inventes
          productos, códigos, números ni fechas.
        - Cada línea es un producto o servicio facturado. No incluyas como líneas los subtotales,
          impuestos (IVA), descuentos generales, envío, retenciones ni totales.
        - description: la descripción tal como aparece, sin corregirla, traducirla ni completarla.
          Si ocupa varios renglones, únelos con un espacio.
        - supplierItemCode: el código de artículo del proveedor si la línea tiene uno; si no, null.
        - quantity y unitPrice: números, sin símbolos de moneda ni separadores de miles. Si la línea tiene
          descuento, unitPrice es el precio unitario después del descuento. Si solo aparece el total de la
          línea, unitPrice = total de la línea / quantity.
        - subtotal: la suma de las líneas antes de impuestos. total: el valor final a pagar.
        - invoiceNumber: el número de la factura tal como aparece (por ejemplo 001-002-000123456).
        - issueDate: la fecha de emisión en formato AAAA-MM-DD.
        """;

    public async Task<Result<ExtractedInvoice>> ExtractAsync(InvoiceDocument document, CancellationToken ct)
    {
        var prepared = PrepareContent(document);
        if (!prepared.IsSuccess)
        {
            return prepared.Error!;
        }

        var (content, pages) = prepared.Value;
        var response = await AskModelAsync(content, ct);
        if (!response.IsSuccess)
        {
            return response.Error!;
        }

        var invoice = response.Value;
        var lines = invoice.Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Description))
            .Select(line => new ExtractedInvoiceLine(
                line.Description.Trim(),
                string.IsNullOrWhiteSpace(line.SupplierItemCode) ? null : line.SupplierItemCode.Trim(),
                line.Quantity,
                line.UnitPrice))
            .ToList();

        var warnings = ReadingWarnings(document, pages, lines);
        return new ExtractedInvoice(
            invoice.InvoiceNumber?.Trim(),
            ParseIsoDate(invoice.IssueDate),
            invoice.SupplierName?.Trim(),
            invoice.Subtotal,
            invoice.Total,
            lines,
            warnings);
    }

    /// <summary>
    /// The message for the model: an image is sent as is; a PDF goes page by page, as text when it
    /// has a usable text layer and as an image when it does not. Also returns the pages, to check
    /// the answer against the text afterwards. A PDF over the reader's limits is rejected here.
    /// </summary>
    private Result<(List<AIContent> Content, List<InvoicePage> Pages)> PrepareContent(InvoiceDocument document)
    {
        if (document.Type != InvoiceFileType.Pdf)
        {
            List<AIContent> image = [new DataContent(document.Content, document.MediaType)];
            return (image, new List<InvoicePage>());
        }

        Result<List<InvoicePage>> read;
        try
        {
            read = PdfPageReader.ReadPages(document.Content, MaxImagesPerRequest);
        }
        catch (Exception ex)
        {
            // Damaged, password-protected or otherwise invalid PDFs: the user's file, not our bug.
            logger.LogWarning(ex, "Could not read the invoice PDF");
            return InvoiceErrors.UnreadableDocument;
        }

        if (!read.IsSuccess)
        {
            return read.Error!;
        }

        var pages = read.Value;
        var content = new List<AIContent>();
        foreach (var page in pages)
        {
            if (page.Text is not null)
            {
                content.Add(new TextContent($"Página {page.Number} (texto):\n{page.Text}"));
            }
            else
            {
                content.Add(new TextContent($"Página {page.Number} (imagen):"));
                content.Add(new DataContent(page.Image!, "image/png"));
            }
        }

        return (content, pages);
    }

    private async Task<Result<LlmInvoice>> AskModelAsync(List<AIContent> content, CancellationToken ct)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instructions),
            new(ChatRole.User, content)
        ];
        var chatOptions = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = options.ReasoningEffort }
        };

        try
        {
            // Structured output: the model must answer with the JSON schema of LlmInvoice.
            var response = await chatClient.GetResponseAsync<LlmInvoice>(messages, chatOptions, cancellationToken: ct);
            if (response.TryGetResult(out var invoice))
            {
                return invoice;
            }

            logger.LogWarning("The model answered with JSON that does not match the invoice schema");
            return InvoiceErrors.UninterpretableResponse;
        }
        catch (ClientResultException ex) when (IsRejectionOfInput(ex.Status))
        {
            // The provider refused this input (too long for the context, too large, an image it
            // cannot open...): retrying the same file fails the same way.
            logger.LogWarning(ex, "The model provider rejected the invoice");
            return InvoiceErrors.ExtractionRejected;
        }
        catch (Exception ex) when (ex is ClientResultException or HttpRequestException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Provider down, bad credentials, rate limit or timeout: not the user's fault, retry later.
            logger.LogError(ex, "The invoice extraction service failed");
            return InvoiceErrors.ExtractionFailed;
        }
    }

    /// <summary>What the user should double-check, depending on how each part of the invoice was read.</summary>
    private static List<string> ReadingWarnings(
        InvoiceDocument document, List<InvoicePage> pages, List<ExtractedInvoiceLine> lines)
    {
        var warnings = new List<string>();

        if (document.Type != InvoiceFileType.Pdf)
        {
            warnings.Add("La factura se leyó desde una imagen; revise con atención las cantidades y los precios.");
            return warnings;
        }

        var scannedPages = pages.Where(page => page.Text is null).Select(page => page.Number).ToList();
        if (scannedPages.Count > 0)
        {
            warnings.Add($"Páginas sin texto legible, leídas como imagen: {string.Join(", ", scannedPages)}. Revise con atención sus cantidades y precios.");
            // A line could come from a scanned page, so the text cannot be used as a reference.
            return warnings;
        }

        // Every page had text: each number of each line must be printed somewhere in it.
        var allText = string.Join('\n', pages.Select(page => page.Text));
        var printed = PrintedNumbers.Find(allText);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (!printed.Contains(line.Quantity))
            {
                warnings.Add($"Línea {lineNumber}: la cantidad {Format(line.Quantity)} no aparece en el texto del PDF; verifíquela.");
            }

            if (!printed.Contains(line.UnitPrice))
            {
                warnings.Add($"Línea {lineNumber}: el precio unitario {Format(line.UnitPrice)} no aparece en el texto del PDF (puede ser calculado); verifíquelo.");
            }
        }

        return warnings;
    }

    /// <summary>
    /// A 4xx answer is about the request, except for the ones that depend on our account or on
    /// timing (bad key, no permission, timeout, rate limit): those are a service failure, retry later.
    /// </summary>
    private static bool IsRejectionOfInput(int status)
    {
        var isClientError = status is >= StatusCodes.FirstClientError and <= StatusCodes.LastClientError;
        var isRetryableOrAccountError = status is StatusCodes.Unauthorized
            or StatusCodes.Forbidden
            or StatusCodes.RequestTimeout
            or StatusCodes.TooManyRequests;
        return isClientError && !isRetryableOrAccountError;
    }

    private static DateOnly? ParseIsoDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // The JSON schema the model must answer with. The date is a string because models are more
    // reliable writing text than a typed date; it is parsed afterwards.
    internal sealed record LlmInvoice(
        string? InvoiceNumber,
        string? IssueDate,
        string? SupplierName,
        decimal? Subtotal,
        decimal? Total,
        List<LlmInvoiceLine> Lines);

    internal sealed record LlmInvoiceLine(
        string Description,
        string? SupplierItemCode,
        decimal Quantity,
        decimal UnitPrice);

    // HTTP status codes: https://www.rfc-editor.org/rfc/rfc9110#name-status-codes
    private static class StatusCodes
    {
        public const int FirstClientError = 400;
        public const int Unauthorized = 401;
        public const int Forbidden = 403;
        public const int RequestTimeout = 408;
        public const int TooManyRequests = 429;
        public const int LastClientError = 499;
    }
}

/// <summary>Used when no API key is configured: the app still starts, and only this feature answers 503.</summary>
internal sealed class UnconfiguredInvoiceExtractor : IInvoiceExtractor
{
    public Task<Result<ExtractedInvoice>> ExtractAsync(InvoiceDocument document, CancellationToken ct) =>
        Task.FromResult<Result<ExtractedInvoice>>(InvoiceErrors.ExtractionNotConfigured);
}
