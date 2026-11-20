using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Api.Setup;

/// <summary>
/// logs the exception with its context and returns a ProblemDetails
/// with a friendly message (never the stack trace). traceId lets support find the log entry.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            // Kestrel stops reading a body larger than the endpoint's limit (e.g. an invoice file).
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                (StatusCodes.Status413PayloadTooLarge, "El archivo supera el tamaño máximo permitido."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "La solicitud no tiene un formato válido."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "El registro fue modificado por otro usuario. Recargue e intente de nuevo."),
            DbUpdateException => (StatusCodes.Status409Conflict, "No se pudo guardar: el dato entra en conflicto con información existente."),
            _ => (StatusCodes.Status500InternalServerError, "Ocurrió un error inesperado. Intente nuevamente más tarde.")
        };

        // Server errors are bugs (Error); client errors are expected noise (Warning).
        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogWarning(exception, "Request failed on {Method} {Path}: {Message}", httpContext.Request.Method, httpContext.Request.Path, exception.Message);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
    }
}
