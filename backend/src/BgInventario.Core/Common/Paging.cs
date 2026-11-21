using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Common;

public record PageQuery
{
    public const int DefaultPageSize = 10;

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
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var totalCount = await query.CountAsync(ct);
        var rowsToSkip = (page.PageNumber - 1) * page.Size;
        var items = await query.Skip(rowsToSkip).Take(page.Size).ToListAsync(ct);

        return new PagedResult<T>(Items: items, page.PageNumber, page.Size, totalCount);
    }
}
