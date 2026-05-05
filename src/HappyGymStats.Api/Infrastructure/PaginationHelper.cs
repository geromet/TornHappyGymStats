using HappyGymStats.Core.Models;

namespace HappyGymStats.Api.Infrastructure;

internal static class PaginationHelper
{
    public static bool TryGetLimit(int? limit, out int take, out string? error)
    {
        if (limit is null)
        {
            take = Pagination.DefaultLimit;
            error = null;
            return true;
        }

        if (limit < 1 || limit > Pagination.MaxLimit)
        {
            take = 0;
            error = $"Limit must be between 1 and {Pagination.MaxLimit}.";
            return false;
        }

        take = limit.Value;
        error = null;
        return true;
    }

    public static CursorPage<T> CreatePage<T>(IReadOnlyList<T> rows, int take, Func<T, PageCursor> cursorSelector)
    {
        var items = rows.Take(take).ToArray();
        var nextCursor = rows.Count > take && items.Length > 0
            ? CursorEncoder.Encode(cursorSelector(items[^1]))
            : null;

        return new CursorPage<T>(items, nextCursor);
    }
}
