using BgInventario.Core.Features.ClientLogs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BgInventario.Api.Endpoints;

public static class ClientLogEndpoints
{
    public static void MapClientLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/client-logs", LogClientError)
            .WithTags("Client logs")
            .WithSummary("Registra un error ocurrido en el frontend")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static NoContent LogClientError(ClientErrorReport report, ClientLogService clientLogs)
    {
        clientLogs.LogError(report);
        return TypedResults.NoContent();
    }
}
