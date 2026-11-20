using Microsoft.Extensions.Caching.Hybrid;

namespace BgInventario.Core.Features.Dashboard;

/// <summary>
/// Cache of the dashboard summary. Its aggregates scan every inventory row, so they are computed
/// once and reused until something that feeds them changes.
///
/// Two rules keep it from showing stale numbers:
/// - Every write to products or suppliers calls <see cref="InvalidateAsync"/> (ProductService, SupplierService).
/// - The entry also expires after <see cref="Expiration"/>, as a safety net for changes that do not
///   go through those services (a script on the database, another API instance).
///
/// HybridCache keeps the entry in memory
/// </summary>
public sealed class DashboardCache(HybridCache cache)
{
    private const string SummaryKey = "dashboard:summary";
    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(5);

    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = Expiration,
        LocalCacheExpiration = Expiration
    };
    /// <summary>
    /// Executes <paramref name="computeSummary"/> if the entry is not in the cache, otherwise returns the cached value.
    /// </summary>
    /// <param name="computeSummary"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public ValueTask<DashboardSummary> GetOrCreateAsync(
        Func<CancellationToken, ValueTask<DashboardSummary>> computeSummary, 
        CancellationToken ct) =>
        cache.GetOrCreateAsync(SummaryKey, computeSummary, EntryOptions, cancellationToken: ct);

    public ValueTask InvalidateAsync(CancellationToken ct) => cache.RemoveAsync(SummaryKey, ct);
}
