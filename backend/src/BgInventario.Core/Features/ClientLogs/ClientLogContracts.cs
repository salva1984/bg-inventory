using System.ComponentModel.DataAnnotations;

namespace BgInventario.Core.Features.ClientLogs;

public static class ClientErrorLimits
{
    public const int MessageMaxLength = 1000;
    public const int StackMaxLength = 4000;
    public const int RouteMaxLength = 500;
}

public sealed record ClientErrorReport(
    [property: Required, StringLength(ClientErrorLimits.MessageMaxLength)] string Message,
    [property: StringLength(ClientErrorLimits.StackMaxLength)] string? Stack,
    [property: Required, StringLength(ClientErrorLimits.RouteMaxLength)] string Route);
