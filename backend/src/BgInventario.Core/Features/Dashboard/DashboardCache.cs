using Microsoft.Extensions.Caching.Hybrid;

namespace BgInventario.Core.Features.Dashboard;

/// <summary>Cache of the dashboard summary; invalidated on every product or supplier write and expired on its own as a safety net.</summary>
public sealed class DashboardCache(HybridCache cache)
{
    private const string SummaryKey = "dashboard:summary";
    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(5);

    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = Expiration,
        LocalCacheExpiration = Expiration
    };
    public ValueTask<DashboardSummary> GetOrCreateAsync(
        Func<CancellationToken, ValueTask<DashboardSummary>> computeSummary, 
        CancellationToken ct) =>
        cache.GetOrCreateAsync(SummaryKey, computeSummary, EntryOptions, cancellationToken: ct);

    public ValueTask InvalidateAsync(CancellationToken ct) => cache.RemoveAsync(SummaryKey, ct);
}
