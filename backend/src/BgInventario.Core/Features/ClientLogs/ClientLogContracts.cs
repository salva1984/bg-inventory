// Request shape of the client log endpoint. Validation attributes run automatically
// before the endpoint (built-in Minimal API validation in .NET 10).
using System.ComponentModel.DataAnnotations;

namespace BgInventario.Core.Features.ClientLogs;

/// <summary>Size limits of a report: they keep a single report from flooding the logs.</summary>
public static class ClientErrorLimits
{
    public const int MessageMaxLength = 1000;
    public const int StackMaxLength = 4000;
    public const int RouteMaxLength = 500;
}

/// <summary>Error that happened in the browser, reported by the frontend.</summary>
public sealed record ClientErrorReport(
    [property: Required, StringLength(ClientErrorLimits.MessageMaxLength)] string Message,
    [property: StringLength(ClientErrorLimits.StackMaxLength)] string? Stack,
    [property: Required, StringLength(ClientErrorLimits.RouteMaxLength)] string Route);
