using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BgInventario.Api.Endpoints;

/// <summary>
/// Receives errors that happen in the browser (they never reach the API on their own) and writes
/// them with Serilog, so frontend and backend errors end up in the same logs and dashboard.
/// Requires a session like every other endpoint: it is not an open door to write logs.
/// </summary>
public static class ClientLogEndpoints
{
    // Log category: lets the dashboard filter frontend errors (SourceContext = "Frontend").
    private const string LogCategory = "Frontend";

    public static void MapClientLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/client-logs", LogClientError)
            .WithTags("Client logs")
            .WithSummary("Registra un error ocurrido en el frontend")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static NoContent LogClientError(ClientErrorReport report, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LogCategory);
        // The request logging middleware already adds the user and IP to this entry.
        logger.LogError(
            "Frontend error on {ClientRoute}: {ClientMessage}{NewLine}{ClientStack}",
            report.Route, report.Message, Environment.NewLine, report.Stack);
        return TypedResults.NoContent();
    }
}

/// <summary>Error reported by the frontend. The limits keep a single report from flooding the logs.</summary>
public sealed record ClientErrorReport(
    [property: Required, StringLength(ClientErrorReport.MessageMaxLength)] string Message,
    [property: StringLength(ClientErrorReport.StackMaxLength)] string? Stack,
    [property: Required, StringLength(ClientErrorReport.RouteMaxLength)] string Route)
{
    public const int MessageMaxLength = 1000;
    public const int StackMaxLength = 4000;
    public const int RouteMaxLength = 500;
}
