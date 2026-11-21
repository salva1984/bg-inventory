using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BgInventario.Api.Setup;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Invoices;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BgInventario.Api.Endpoints;

public static class InvoiceEndpoints
{
    // Caps the memory used to read the file whole (InvoiceReceiptLimits.MaxFileBytes).
    private const long MaxFileBytes = InvoiceReceiptLimits.MaxFileBytes;

    private const long MultipartOverheadBytes = 64 * 1024;

    private const long Base64BytesPerGroup = 4;
    private const long BinaryBytesPerGroup = 3;
    private const long MaxBase64FileBytes = (MaxFileBytes + BinaryBytesPerGroup - 1) / BinaryBytesPerGroup * Base64BytesPerGroup;
    private const long ConfirmLinesOverheadBytes = 1024 * 1024;

    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices")
            .WithTags("Invoices")
            .RequireAuthorization(ServiceCollectionExtensions.AdminPolicy)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/analyze", Analyze)
            .WithSummary("Lee una factura (PDF o imagen) y propone el producto de cada línea; no guarda nada")
            // Token in a header, not a cookie, so no CSRF.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxFileBytes + MultipartOverheadBytes))
            .RequireRateLimiting(ServiceCollectionExtensions.InvoiceAnalysisRateLimit)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/confirm", Confirm)
            .WithSummary("Registra el stock de las líneas revisadas, guarda las equivalencias del proveedor y la factura en el historial")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBase64FileBytes + ConfirmLinesOverheadBytes))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", GetPaged)
            .WithSummary("Historial paginado de facturas confirmadas, la más reciente primero");
        group.MapGet("/{id:int}", GetById)
            .WithSummary("Detalle de una factura confirmada: cada línea con el producto al que se asoció")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:int}/file", GetFile)
            .WithSummary("Descarga el archivo original de una factura confirmada")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<InvoiceAnalysis>, ProblemHttpResult>> Analyze(
        IFormFile file, [FromForm] int supplierId, InvoiceService invoices, CancellationToken ct)
    {
        var exceedsLimit = file.Length > MaxFileBytes;
        if (exceedsLimit)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: $"El archivo supera el límite de {InvoiceReceiptLimits.MaxFileMegabytes} MB.");
        }

        var content = await ReadAllBytesAsync(file, ct);
        var result = await invoices.AnalyzeAsync(supplierId, content, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<ConfirmedInvoice>, ProblemHttpResult>> Confirm(
        ConfirmInvoiceRequest request, ClaimsPrincipal user, InvoiceService invoices, CancellationToken ct)
    {
        var userId = int.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var result = await invoices.ConfirmAsync(request, userId, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Ok<PagedResult<InvoiceReceiptItem>>> GetPaged(
        [AsParameters] InvoiceReceiptQuery query, InvoiceReceiptService receipts, CancellationToken ct) =>
        TypedResults.Ok(await receipts.GetPagedAsync(query, ct));

    private static async Task<Results<Ok<InvoiceReceiptDetail>, ProblemHttpResult>> GetById(
        int id, InvoiceReceiptService receipts, CancellationToken ct)
    {
        var result = await receipts.GetByIdAsync(id, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetFile(
        int id, InvoiceReceiptService receipts, CancellationToken ct)
    {
        var result = await receipts.GetFileAsync(id, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var file = result.Value;
        return TypedResults.File(file.Content, file.ContentType, file.FileName);
    }

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
