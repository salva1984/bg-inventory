using BgInventario.Api.Setup;
using BgInventario.Core.Features.Assistant;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BgInventario.Api.Endpoints;

public static class AssistantEndpoints
{
    // 20 messages of 4,000 characters at up to 3 bytes each, plus the JSON around them.
    private const long MaxRequestBytes = 512 * 1024;

    public static void MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assistant/chat", Chat)
            .WithTags("Assistant")
            .WithSummary("Responde una pregunta sobre el inventario consultando la base de datos; no modifica nada. Se envía la conversación completa")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequireRateLimiting(ServiceCollectionExtensions.AssistantRateLimit)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<AssistantReply>, ProblemHttpResult>> Chat(
        AssistantRequest request, IInventoryAssistant assistant, CancellationToken ct)
    {
        var result = await assistant.AskAsync(request.Messages, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }
}
