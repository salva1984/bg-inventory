// Server-side pagination shared by every listing endpoint.
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Common;

/// <summary>Query-string paging. Nullable so they are optional when bound with [AsParameters].</summary>
public record PageQuery
{
    public const int DefaultPageSize = 10;

    // Upper bound so a client cannot ask for the whole table in a single request.
    public const int MaxPageSize = 100;

    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    internal int PageNumber => Page ?? 1;
    internal int Size => PageSize ?? DefaultPageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    /// <summary>Runs COUNT + OFFSET/FETCH on the server. The query should already be sorted and projected to a DTO.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var totalCount = await query.CountAsync(ct);
        var rowsToSkip = (page.PageNumber - 1) * page.Size;
        var items = await query.Skip(rowsToSkip).Take(page.Size).ToListAsync(ct);

        return new PagedResult<T>(items, page.PageNumber, page.Size, totalCount);
    }
}
