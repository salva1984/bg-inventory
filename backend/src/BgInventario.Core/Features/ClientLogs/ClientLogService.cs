using Microsoft.Extensions.Logging;

namespace BgInventario.Core.Features.ClientLogs;

/// <summary>
/// Writes the errors that happen in the browser to the same logs as the backend errors.
/// They never reach the API on their own: the frontend reports them.
/// </summary>
public sealed class ClientLogService(ILogger<ClientLogService> logger)
{
    public void LogError(ClientErrorReport report)
    {
        // The request logging middleware already adds the user and IP to this entry.
        logger.LogError(
            "Frontend error on {ClientRoute}: {ClientMessage}{NewLine}{ClientStack}",
            report.Route, report.Message, Environment.NewLine, report.Stack);
    }
}
