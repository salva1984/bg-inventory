// Shapes of the invoice flow: the file that comes in, what the extractor reads from it, the
// analysis returned for review, the confirmation that finally touches the inventory, and the
// history of confirmed invoices.
using System.ComponentModel.DataAnnotations;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Suppliers;

namespace BgInventario.Core.Features.Invoices;

// ---------- Extraction (implemented in Infrastructure) ----------

/// <summary>Reads an invoice of any layout and returns its data. The implementation uses an LLM.</summary>
public interface IInvoiceExtractor
{
    Task<Result<ExtractedInvoice>> ExtractAsync(InvoiceDocument document, CancellationToken ct);
}

public enum InvoiceFileType
{
    Pdf,
    Png,
    Jpeg,
    Webp
}

/// <summary>An uploaded invoice whose type was checked by its content, not by its name.</summary>
public sealed record InvoiceDocument(byte[] Content, InvoiceFileType Type)
{
    public string MediaType => Type switch
    {
        InvoiceFileType.Pdf => "application/pdf",
        InvoiceFileType.Png => "image/png",
        InvoiceFileType.Jpeg => "image/jpeg",
        _ => "image/webp"
    };

    // File signatures ("magic numbers"): https://en.wikipedia.org/wiki/List_of_file_signatures
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();
    private static readonly byte[] WebpSignature = "WEBP"u8.ToArray();

    // A WebP file is a RIFF container: "RIFF", 4 bytes of size, then "WEBP".
    private const int WebpSignatureOffset = 8;

    /// <summary>Type of the file by its first bytes; null when it is not a supported format.</summary>
    public static InvoiceFileType? DetectType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PdfSignature))
        {
            return InvoiceFileType.Pdf;
        }

        if (content.StartsWith(PngSignature))
        {
            return InvoiceFileType.Png;
        }

        if (content.StartsWith(JpegSignature))
        {
            return InvoiceFileType.Jpeg;
        }

        var isWebp = content.StartsWith(RiffSignature)
            && content.Length >= WebpSignatureOffset + WebpSignature.Length
            && content.Slice(WebpSignatureOffset, WebpSignature.Length).SequenceEqual(WebpSignature);
        return isWebp ? InvoiceFileType.Webp : null;
    }
}

/// <summary>What the invoice says, without interpretation. Missing data stays null.</summary>
public sealed record ExtractedInvoice(
    string? InvoiceNumber,
    DateOnly? IssueDate,
    string? SupplierName,
    decimal? Subtotal,
    decimal? Total,
    IReadOnlyList<ExtractedInvoiceLine> Lines,
    // Doubts found while reading (a page read as an image, a number not found in the text...).
    IReadOnlyList<string> Warnings);

public sealed record ExtractedInvoiceLine(
    string Description,
    string? SupplierItemCode,
    decimal Quantity,
    decimal UnitPrice);

// ---------- Analysis (nothing is saved) ----------

public enum LineMatchStatus
{
    /// <summary>Identified by a code or a confirmed equivalence; preselected.</summary>
    Matched,

    /// <summary>The best candidate is textually close; the user confirms it.</summary>
    Suggested,

    /// <summary>Only weak candidates; the user must check them.</summary>
    NeedsReview,

    /// <summary>The description says too little or nothing resembles it; the user identifies the product.</summary>
    Manual
}

/// <summary>Why a line is <see cref="LineMatchStatus.Matched"/>.</summary>
public enum MatchedBy
{
    SupplierItemCode,
    ProductCode,
    Description
}

/// <summary>A possible product for a line. Score goes from 0 to 1 (1 = known equivalence).</summary>
public sealed record ProductCandidate(int ProductId, string Code, string Name, double Score);

/// <summary>Candidates come best first; for Matched lines the first one is the identified product.</summary>
public sealed record LineMatch(LineMatchStatus Status, MatchedBy? MatchedBy, IReadOnlyList<ProductCandidate> Candidates);

public sealed record AnalyzedInvoiceLine(int LineNumber, ExtractedInvoiceLine Extracted, LineMatch Match);

/// <summary>Whether the issuer printed on the invoice is the supplier the user chose.</summary>
public enum IssuerCheck
{
    /// <summary>The invoice was issued by the chosen supplier.</summary>
    Matches,

    /// <summary>The issuer could not be read; there is nothing to compare.</summary>
    NotRead,

    /// <summary>The issuer is another registered supplier (see <see cref="InvoiceAnalysis.SuggestedSupplier"/>).</summary>
    OtherSupplier,

    /// <summary>The issuer is not a registered supplier.</summary>
    NotRegistered
}

/// <summary>
/// The lines were matched with the aliases of <see cref="SupplierId"/>, the supplier the user chose.
/// When <see cref="IssuerCheck"/> is not Matches, that choice may be wrong and the user must decide
/// before confirming: the stock and the aliases would go to that supplier.
/// </summary>
public sealed record InvoiceAnalysis(
    int SupplierId,
    string? InvoiceNumber,
    DateOnly? IssueDate,
    // The issuer as printed on the invoice.
    string? SupplierName,
    IssuerCheck IssuerCheck,
    // The registered supplier that matches the issuer, when it is not the chosen one.
    SupplierLookup? SuggestedSupplier,
    decimal? Subtotal,
    decimal? Total,
    IReadOnlyList<AnalyzedInvoiceLine> Lines,
    IReadOnlyList<string> Warnings);

// ---------- Confirmation (the only step that writes) ----------

/// <summary>
/// Lines already reviewed by the user, each one with the product it really is. The invoice file
/// travels again (base64 in the JSON) so the history keeps the original next to what was registered.
/// </summary>
public sealed record ConfirmInvoiceRequest(
    [property: Range(1, int.MaxValue)] int SupplierId,
    // Becomes the lot number of the received stock, so it has the lot's size limit.
    [property: Required, StringLength(InventoryLotLimits.LotNumberMaxLength)] string InvoiceNumber,
    DateOnly? IssueDate,
    // The total with taxes as the analysis read it. Only shown in the history.
    [property: Range(typeof(decimal), "0", InvoiceReceiptLimits.MaxAmount)] decimal? InvoiceTotal,
    [property: Required] InvoiceFileUpload File,
    [property: Required, MinLength(1)] IReadOnlyList<ConfirmInvoiceLineRequest> Lines,
    // The user was told the invoice is already registered and wants to register it again:
    // its stock is added to the existing lots and the history keeps both records.
    bool RegisterAgain = false);

/// <summary>The analyzed file. Its type is not sent: the service detects it from the content.</summary>
public sealed record InvoiceFileUpload(
    [property: Required, StringLength(InvoiceReceiptLimits.FileNameMaxLength)] string FileName,
    [property: Required] byte[] Content);

public sealed record ConfirmInvoiceLineRequest(
    [property: Range(1, int.MaxValue)] int ProductId,
    // The description as it came in the invoice: it is what the next invoice will be matched against.
    [property: Required, StringLength(SupplierProductAliasLimits.DescriptionMaxLength)] string Description,
    [property: StringLength(SupplierProductAliasLimits.SupplierItemCodeMaxLength)] string? SupplierItemCode,
    [property: Range(1, int.MaxValue)] int Quantity,
    [property: Range(InventoryLotLimits.MinPrice, InventoryLotLimits.MaxPrice)] decimal UnitPrice,
    // The user created the product while reviewing this invoice. Only a label for the history.
    bool ProductCreated = false);

public sealed record ConfirmedInvoice(
    int ReceiptId, string LotNumber, int ProductsReceived, int UnitsReceived, int AliasesSaved);

// ---------- History of confirmed invoices ----------

public sealed record InvoiceReceiptQuery : PageQuery
{
    /// <summary>Part of the invoice number or of the supplier name.</summary>
    public string? Search { get; init; }
}

public sealed record InvoiceReceiptItem(
    int Id,
    string InvoiceNumber,
    string SupplierName,
    DateOnly? IssueDate,
    DateTime RegisteredAt,
    string RegisteredBy,
    int LineCount,
    decimal Subtotal,
    decimal? InvoiceTotal,
    string FileName);

public sealed record InvoiceReceiptLineItem(
    int LineNumber,
    // As the invoice wrote it, and the product it was mapped to.
    string Description,
    string? SupplierItemCode,
    int ProductId,
    string ProductCode,
    string ProductName,
    bool ProductCreated,
    int Quantity,
    decimal UnitPrice);

public sealed record InvoiceReceiptDetail(
    int Id,
    string InvoiceNumber,
    string SupplierName,
    DateOnly? IssueDate,
    DateTime RegisteredAt,
    string RegisteredBy,
    decimal Subtotal,
    decimal? InvoiceTotal,
    string FileName,
    IReadOnlyList<InvoiceReceiptLineItem> Lines);

public sealed record InvoiceReceiptFile(string FileName, string ContentType, byte[] Content);

// ---------- Errors ----------

/// <summary>Errors of the invoice flow, shared with the extractor in Infrastructure.</summary>
public static class InvoiceErrors
{
    public static readonly ErrorDetail UnknownSupplier = ErrorDetail.Validation("invoice.unknown_supplier", "El proveedor no existe.");
    public static readonly ErrorDetail UnknownProduct = ErrorDetail.Validation("invoice.unknown_product", "Uno o más productos no existen.");
    public static readonly ErrorDetail ReceiptNotFound = ErrorDetail.NotFound("invoice.not_found", "La factura no existe.");
    public static readonly ErrorDetail FileTooLarge = ErrorDetail.Validation("invoice.file_too_large", $"El archivo supera el límite de {InvoiceReceiptLimits.MaxFileMegabytes} MB.");
    public static readonly ErrorDetail UnsupportedFile = ErrorDetail.Validation("invoice.unsupported_file", "El archivo debe ser un PDF o una imagen PNG, JPEG o WebP.");
    public static readonly ErrorDetail UnreadableDocument = ErrorDetail.Validation("invoice.unreadable", "No se pudo leer el documento. Verifique que el archivo no esté dañado ni protegido con contraseña.");
    public static readonly ErrorDetail QuantityTooLarge = ErrorDetail.Validation("invoice.quantity_too_large", $"La factura suma más de {int.MaxValue} unidades; revise las cantidades.");
    public static readonly ErrorDetail NoLines = ErrorDetail.Validation("invoice.no_lines", "No se encontraron líneas de productos en la factura.");
    public static readonly ErrorDetail DocumentTooLarge = ErrorDetail.Validation("invoice.document_too_large", "La factura tiene demasiadas páginas o demasiado texto para analizarla en una sola solicitud.");
    public static readonly ErrorDetail TooManyScannedPages = ErrorDetail.Validation("invoice.too_many_scanned_pages", "La factura tiene demasiadas páginas escaneadas para analizarlas en una sola solicitud.");
    public static readonly ErrorDetail AlreadyRegistered = ErrorDetail.Conflict("invoice.already_registered", "Esta factura ya fue registrada para el proveedor. Confírmala de nuevo solo si quieres volver a registrarla.");
    public static readonly ErrorDetail ExtractionNotConfigured = ErrorDetail.Unavailable("invoice.extraction_not_configured", "El análisis de facturas no está configurado en el servidor.");
    public static readonly ErrorDetail ExtractionFailed = ErrorDetail.Unavailable("invoice.extraction_failed", "El servicio de análisis de facturas no respondió. Intente nuevamente en unos minutos.");
    public static readonly ErrorDetail ExtractionRejected = ErrorDetail.Validation("invoice.extraction_rejected", "El servicio de análisis no pudo procesar este documento.");
    public static readonly ErrorDetail UninterpretableResponse = ErrorDetail.Validation("invoice.uninterpretable", "No se pudo interpretar la factura. Intente con un archivo más legible.");
}
