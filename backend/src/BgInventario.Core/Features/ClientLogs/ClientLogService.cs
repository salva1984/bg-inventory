using Microsoft.Extensions.Logging;

namespace BgInventario.Core.Features.ClientLogs;

public sealed class ClientLogService(ILogger<ClientLogService> logger)
{
    public void LogError(ClientErrorReport report)
    {
        logger.LogError(
            "Frontend error on {ClientRoute}: {ClientMessage}{NewLine}{ClientStack}",
            report.Route, report.Message, Environment.NewLine, report.Stack);
    }
}
