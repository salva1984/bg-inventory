using BgInventario.Core.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BgInventario.Api.Setup;

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
