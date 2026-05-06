using HappyGymStats.AdminPanel.Infrastructure;
using HappyGymStats.Core.Models;
using HappyGymStats.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.AdminPanel.Controllers;

[Route("admin/api/v1/users")]
public sealed class AdminUsersController(HappyGymStatsDbContext db) : AdminApiControllerBase
{
    // GET /admin/api/v1/users?limit=50&offset=0
    // Lists all AnonymousIds that have gym train records, with aggregate stats.
    [HttpGet]
    public async Task<IActionResult> ListUsers(int? limit, int? offset, CancellationToken ct)
    {
        var take = ClampLimit(limit);
        var skip = Math.Max(offset ?? 0, 0);

        var rows = await db.UserLogEntries
            .AsNoTracking()
            .Where(e => e.HappyUsed != null)
            .GroupBy(e => e.AnonymousId)
            .Select(g => new
            {
                AnonymousId = g.Key,
                TrainCount = g.Count(),
                LastTrainAtUtc = g.Max(e => e.OccurredAtUtc),
            })
            .OrderBy(s => s.AnonymousId)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        var items = hasMore ? rows.Take(take).ToList() : rows;

        return Ok(new
        {
            items,
            hasMore,
            nextOffset = hasMore ? skip + take : (int?)null,
        });
    }

    // GET /admin/api/v1/users/{anonymousId}/gym-trains?limit=50&cursor=
    // Returns paginated gym trains for any user. No identity exposed — AnonymousId only.
    [HttpGet("{anonymousId:guid}/gym-trains")]
    public async Task<IActionResult> GetUserGymTrains(Guid anonymousId, int? limit, string? cursor, CancellationToken ct)
    {
        var take = ClampLimit(limit);

        if (!CursorEncoder.TryDecode(cursor, out var pageCursor))
            return ApiError(StatusCodes.Status422UnprocessableEntity, "validation_failed", "Cursor is invalid.");

        IQueryable<HappyGymStats.Data.Entities.UserLogEntryEntity> baseQuery;
        if (pageCursor is null)
        {
            baseQuery = db.UserLogEntries.AsNoTracking()
                .Where(x => x.AnonymousId == anonymousId && x.HappyUsed != null);
        }
        else
        {
            baseQuery = db.UserLogEntries.AsNoTracking()
                .Where(x => x.AnonymousId == anonymousId
                    && x.HappyUsed != null
                    && (x.OccurredAtUtc < pageCursor.OccurredAtUtc
                        || (x.OccurredAtUtc == pageCursor.OccurredAtUtc
                            && string.Compare(x.LogEntryId, pageCursor.Id, StringComparison.Ordinal) < 0)));
        }

        var rows = await baseQuery
            .OrderByDescending(row => row.OccurredAtUtc)
            .ThenByDescending(row => row.LogEntryId)
            .Take(take + 1)
            .Select(row => new GymTrainDto(
                row.LogEntryId,
                row.OccurredAtUtc,
                row.HappyBeforeTrain,
                row.HappyBeforeTrain != null && row.HappyUsed != null ? row.HappyBeforeTrain - row.HappyUsed : null,
                row.HappyUsed,
                row.MaxHappyBefore,
                row.MaxHappyAfter))
            .ToListAsync(ct);

        var items = rows.Count > take ? rows.Take(take).ToList() : rows;
        string? nextCursor = rows.Count > take && items.Count > 0
            ? CursorEncoder.Encode(new PageCursor(items[^1].OccurredAtUtc, items[^1].LogId))
            : null;

        return Ok(new CursorPage<GymTrainDto>(items, nextCursor));
    }
}
