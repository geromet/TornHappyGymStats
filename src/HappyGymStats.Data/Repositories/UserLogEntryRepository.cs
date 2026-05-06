using HappyGymStats.Core.Models;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class UserLogEntryRepository(HappyGymStatsDbContext db) : IUserLogEntryRepository
{
    public Task<HashSet<string>> GetExistingLogIdsAsync(Guid anonymousId, CancellationToken ct)
        => db.UserLogEntries
            .AsNoTracking()
            .Where(r => r.AnonymousId == anonymousId)
            .Select(r => r.LogEntryId)
            .ToHashSetAsync(ct);

    public Task AddRangeAsync(IEnumerable<UserLogEntryEntity> entries, CancellationToken ct)
    {
        db.UserLogEntries.AddRange(entries);
        return Task.CompletedTask;
    }

    public async Task StageHappyBeforeTrainBatchAsync(Guid anonymousId, IReadOnlyList<HappyBeforeTrainUpdate> updates, CancellationToken ct)
    {
        if (updates.Count == 0) return;
        var logIds = updates.Select(u => u.LogId).ToHashSet(StringComparer.Ordinal);
        var entries = await db.UserLogEntries
            .Where(e => e.AnonymousId == anonymousId && logIds.Contains(e.LogEntryId))
            .ToListAsync(ct);
        var byLogId = updates.ToDictionary(u => u.LogId, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!byLogId.TryGetValue(entry.LogEntryId, out var upd)) continue;
            entry.HappyBeforeTrain = upd.HappyBeforeTrain;
            entry.HappyBeforeDelta = entry.HappyBeforeApi.HasValue && upd.HappyBeforeTrain.HasValue
                ? entry.HappyBeforeApi.Value - upd.HappyBeforeTrain.Value
                : null;
        }
        // No save — caller commits via IUnitOfWork
    }

    public Task<IReadOnlyList<ReconstructionLogRecord>> GetReconstructionRecordsAsync(Guid anonymousId, CancellationToken ct)
        => db.UserLogEntries
            .AsNoTracking()
            .Where(row => row.AnonymousId == anonymousId)
            .OrderBy(row => row.OccurredAtUtc)
            .Select(row => new ReconstructionLogRecord(
                row.LogEntryId,
                row.OccurredAtUtc,
                null,
                row.LogTypeId,
                row.HappyUsed,
                row.HappyIncreased,
                row.HappyDecreased,
                row.MaxHappyBefore,
                row.MaxHappyAfter,
                row.PropertyId))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ReconstructionLogRecord>)t.Result, TaskContinuationOptions.ExecuteSynchronously);

    public Task<IReadOnlyList<GymLogEntry>> GetGymLogEntriesAsync(CancellationToken ct)
        => db.UserLogEntries
            .AsNoTracking()
            .Where(x => x.HappyUsed != null)
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => new GymLogEntry(
                x.LogEntryId,
                x.OccurredAtUtc,
                x.HappyBeforeTrain,
                x.EnergyUsed,
                x.StrengthBefore, x.StrengthIncreased,
                x.DefenseBefore, x.DefenseIncreased,
                x.SpeedBefore, x.SpeedIncreased,
                x.DexterityBefore, x.DexterityIncreased))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<GymLogEntry>)t.Result, TaskContinuationOptions.ExecuteSynchronously);

    public async Task<CursorPage<GymTrainDto>> GetGymTrainsPageAsync(int take, PageCursor? cursor, CancellationToken ct)
    {
        IQueryable<UserLogEntryEntity> baseQuery;
        if (cursor is null)
        {
            baseQuery = db.UserLogEntries.AsNoTracking().Where(x => x.HappyUsed != null);
        }
        else
        {
            baseQuery = db.UserLogEntries
                .FromSqlInterpolated($@"
SELECT *
FROM UserLogEntries
WHERE HappyUsed IS NOT NULL
  AND (OccurredAtUtc < {cursor.OccurredAtUtc.UtcDateTime}
   OR (OccurredAtUtc = {cursor.OccurredAtUtc.UtcDateTime} AND LogEntryId < {cursor.Id}))")
                .AsNoTracking();
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
        return new CursorPage<GymTrainDto>(items, nextCursor);
    }

    public async Task<CursorPage<GymTrainDto>> GetGymTrainsPageAsync(Guid anonymousId, int take, PageCursor? cursor, CancellationToken ct)
    {
        var baseQuery = cursor is null
            ? db.UserLogEntries.AsNoTracking()
                .Where(x => x.AnonymousId == anonymousId && x.HappyUsed != null)
            : db.UserLogEntries.AsNoTracking()
                .Where(x => x.AnonymousId == anonymousId
                    && x.HappyUsed != null
                    && (x.OccurredAtUtc < cursor.OccurredAtUtc
                        || (x.OccurredAtUtc == cursor.OccurredAtUtc
                            && string.Compare(x.LogEntryId, cursor.Id, StringComparison.Ordinal) < 0)));

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
        return new CursorPage<GymTrainDto>(items, nextCursor);
    }
}
