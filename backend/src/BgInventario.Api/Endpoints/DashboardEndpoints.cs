using BgInventario.Core.Features.Dashboard;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BgInventario.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", GetSummary)
            .WithTags("Dashboard")
            .WithSummary("Totales del inventario, valor por proveedor, menor stock y mayor diferencia de precio")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Ok<DashboardSummary>> GetSummary(DashboardService dashboard, CancellationToken ct) =>
        TypedResults.Ok(await dashboard.GetSummaryAsync(ct));
}
