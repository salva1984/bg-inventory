using BgInventario.Core.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BgInventario.Api.Setup;

/// <summary>
/// Single place where business errors become HTTP responses (RFC 7807 ProblemDetails).
/// The "code" extension lets the frontend react to a specific error without parsing the message.
/// </summary>
public static class ResultExtensions
{
    public static ProblemHttpResult ToProblem(this ErrorDetail error) =>
        TypedResults.Problem(
            statusCode: error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError
            },
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
