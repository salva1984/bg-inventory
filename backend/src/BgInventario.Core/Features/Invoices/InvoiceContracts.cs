using System.ComponentModel.DataAnnotations;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Suppliers;

namespace BgInventario.Core.Features.Invoices;

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

public sealed record InvoiceDocument(byte[] Content, InvoiceFileType Type)
{
    public bool IsImage => Type != InvoiceFileType.Pdf;

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

public sealed record ExtractedInvoice(
    string? InvoiceNumber,
    DateOnly? IssueDate,
    string? SupplierName,
    decimal? Subtotal,
    decimal? Total,
    IReadOnlyList<ExtractedInvoiceLine> Lines,
    IReadOnlyList<string> Warnings);

public sealed record ExtractedInvoiceLine(
    string Description,
    string? SupplierItemCode,
    decimal Quantity,
    decimal UnitPrice);

public enum LineMatchStatus
{
    Matched,
    Suggested,
    NeedsReview,
    Manual
}

public enum MatchedBy
{
    SupplierItemCode,
    ProductCode,
    Description
}

public sealed record ProductCandidate(int ProductId, string Code, string Name, double Score);

/// <summary>Candidates come best first; for Matched lines the first one is the identified product.</summary>
public sealed record LineMatch(LineMatchStatus Status, MatchedBy? MatchedBy, IReadOnlyList<ProductCandidate> Candidates);

public sealed record AnalyzedInvoiceLine(int LineNumber, ExtractedInvoiceLine Extracted, LineMatch Match);

public enum IssuerCheck
{
    Matches,
    NotRead,
    OtherSupplier,
    NotRegistered
}

/// <summary>
/// Lines matched with the aliases of <see cref="SupplierId"/>, the supplier the user chose. If
/// <see cref="IssuerCheck"/> is not Matches that choice may be wrong: stock and aliases go to it.
/// </summary>
public sealed record InvoiceAnalysis(
    int SupplierId,
    string? InvoiceNumber,
    DateOnly? IssueDate,
    string? SupplierName,
    IssuerCheck IssuerCheck,
    SupplierLookup? SuggestedSupplier,
    decimal? Subtotal,
    decimal? Total,
    IReadOnlyList<AnalyzedInvoiceLine> Lines,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Lines reviewed by the user. The invoice file travels again (base64) so the history keeps the original.
/// </summary>
public sealed record ConfirmInvoiceRequest(
    [property: Range(1, int.MaxValue)] int SupplierId,
    // Becomes the lot number, so it has the lot's size limit.
    [property: Required, StringLength(InventoryLotLimits.LotNumberMaxLength)] string InvoiceNumber,
    DateOnly? IssueDate,
    // Total with taxes as read by the analysis; only shown in the history.
    [property: Range(typeof(decimal), "0", InvoiceReceiptLimits.MaxAmount)] decimal? InvoiceTotal,
    [property: Required] InvoiceFileUpload File,
    [property: Required, MinLength(1)] IReadOnlyList<ConfirmInvoiceLineRequest> Lines,
    bool RegisterAgain = false);

public sealed record InvoiceFileUpload(
    [property: Required, StringLength(InvoiceReceiptLimits.FileNameMaxLength)] string FileName,
    [property: Required] byte[] Content);

public sealed record ConfirmInvoiceLineRequest(
    [property: Range(1, int.MaxValue)] int ProductId,
    [property: Required, StringLength(SupplierProductAliasLimits.DescriptionMaxLength)] string OriginalDescription,
    [property: StringLength(SupplierProductAliasLimits.SupplierItemCodeMaxLength)] string? SupplierItemCode,
    [property: Range(1, int.MaxValue)] int Quantity,
    [property: Range(InventoryLotLimits.MinPrice, InventoryLotLimits.MaxPrice)] decimal UnitPrice,
    bool ProductCreated = false);

public sealed record ConfirmedInvoice(
    int ReceiptId, string LotNumber, int ProductsReceived, int UnitsReceived, int AliasesSaved);

public sealed record InvoiceReceiptQuery : PageQuery
{
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
