using GlobalRubber.MMM.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// Paging shared by the read-only report repositories: one COUNT over the filtered entities plus one page query, or the
/// whole (already filtered and ordered) set when <c>page</c> is null (export).
/// </summary>
internal static class ReportPaging
{
    public static async Task<(IReadOnlyList<T> Items, int TotalCount)> PageAsync<TEntity, T>(
        IQueryable<TEntity> filtered, IQueryable<T> rows, ReportPage? page, CancellationToken cancellationToken)
    {
        if (page is null)
        {
            var all = await rows.ToListAsync(cancellationToken);
            return (all, all.Count);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var items = totalCount == 0
            ? new List<T>()
            : await rows.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
