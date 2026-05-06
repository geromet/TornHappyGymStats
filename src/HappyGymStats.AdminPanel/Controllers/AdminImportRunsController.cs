using HappyGymStats.AdminPanel.Infrastructure;
using HappyGymStats.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.AdminPanel.Controllers;

[Route("admin/api/v1/import-runs")]
public sealed class AdminImportRunsController(HappyGymStatsDbContext db) : AdminApiControllerBase
{
    // GET /admin/api/v1/import-runs?limit=50&offset=0
    // Returns import run history. AnonymousIds are visible; no player identity is exposed.
    [HttpGet]
    public async Task<IActionResult> ListImportRuns(int? limit, int? offset, CancellationToken ct)
    {
        var take = ClampLimit(limit);
        var skip = Math.Max(offset ?? 0, 0);

        var rows = await db.ImportRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAtUtc)
            .Skip(skip)
            .Take(take + 1)
            .Select(r => new
            {
                r.Id,
                r.AnonymousId,
                r.Outcome,
                r.StartedAtUtc,
                r.CompletedAtUtc,
                r.PagesFetched,
                r.LogsFetched,
                r.LogsAppended,
                r.ErrorMessage,
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        var items = hasMore ? rows.Take(take).ToList() : rows;

        return Ok(new { items, hasMore, nextOffset = hasMore ? skip + take : (int?)null });
    }
}
