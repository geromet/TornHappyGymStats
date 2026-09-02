using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class WarStateRepository(HappyGymStatsDbContext db) : IWarStateRepository
{
    public Task<WarCurrentEntity?> GetCurrentAsync(string scopeKey, CancellationToken ct)
        => db.WarCurrent
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.ScopeKey == scopeKey, ct);

    public async Task UpsertCurrentAsync(WarCurrentEntity current, CancellationToken ct)
    {
        var tracked = db.WarCurrent.Local.FirstOrDefault(e => e.ScopeKey == current.ScopeKey);
        if (tracked is not null)
        {
            CopyCurrent(current, tracked);
            return;
        }

        var existing = await db.WarCurrent.SingleOrDefaultAsync(e => e.ScopeKey == current.ScopeKey, ct);
        if (existing is null)
        {
            db.WarCurrent.Add(current);
            return;
        }

        CopyCurrent(current, existing);
    }

    public async Task<IReadOnlyList<WarRosterSnapshotEntity>> GetRosterSnapshotAsync(long warId, CancellationToken ct)
    {
        return await db.WarRosterSnapshots
            .AsNoTracking()
            .Where(e => e.WarId == warId)
            .OrderBy(e => e.FactionId)
            .ThenBy(e => e.MemberId)
            .ToListAsync(ct);
    }

    public async Task ReplaceRosterSnapshotAsync(long warId, IReadOnlyCollection<WarRosterSnapshotEntity> rosterEntries, CancellationToken ct)
    {
        var tracked = db.WarRosterSnapshots.Local.Where(e => e.WarId == warId).ToList();
        if (tracked.Count > 0)
            db.WarRosterSnapshots.RemoveRange(tracked);

        var persisted = await db.WarRosterSnapshots.Where(e => e.WarId == warId).ToListAsync(ct);
        if (persisted.Count > 0)
            db.WarRosterSnapshots.RemoveRange(persisted);

        if (rosterEntries.Count > 0)
            await db.WarRosterSnapshots.AddRangeAsync(rosterEntries, ct);
    }

    public async Task<IReadOnlyList<WarScoreSampleEntity>> GetScoreSamplesAsync(long warId, CancellationToken ct)
    {
        return await db.WarScoreSamples
            .AsNoTracking()
            .Where(e => e.WarId == warId)
            .OrderBy(e => e.SampledAtUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
    }

    public Task AddScoreSampleAsync(WarScoreSampleEntity sample, CancellationToken ct)
    {
        db.WarScoreSamples.Add(sample);
        return Task.CompletedTask;
    }

    public Task<WarPollerHeartbeatEntity?> GetHeartbeatAsync(string scopeKey, CancellationToken ct)
        => db.WarPollerHeartbeats
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.ScopeKey == scopeKey, ct);

    public async Task UpsertHeartbeatAsync(WarPollerHeartbeatEntity heartbeat, CancellationToken ct)
    {
        var tracked = db.WarPollerHeartbeats.Local.FirstOrDefault(e => e.ScopeKey == heartbeat.ScopeKey);
        if (tracked is not null)
        {
            CopyHeartbeat(heartbeat, tracked);
            return;
        }

        var existing = await db.WarPollerHeartbeats.SingleOrDefaultAsync(e => e.ScopeKey == heartbeat.ScopeKey, ct);
        if (existing is null)
        {
            db.WarPollerHeartbeats.Add(heartbeat);
            return;
        }

        CopyHeartbeat(heartbeat, existing);
    }

    private static void CopyCurrent(WarCurrentEntity source, WarCurrentEntity target)
    {
        target.WarId = source.WarId;
        target.FactionId = source.FactionId;
        target.FactionName = source.FactionName;
        target.OpponentFactionId = source.OpponentFactionId;
        target.OpponentFactionName = source.OpponentFactionName;
        target.StartedAtUtc = source.StartedAtUtc;
        target.EndsAtUtc = source.EndsAtUtc;
        target.IsLive = source.IsLive;
        target.ObservedAtUtc = source.ObservedAtUtc;
    }

    private static void CopyHeartbeat(WarPollerHeartbeatEntity source, WarPollerHeartbeatEntity target)
    {
        target.Phase = source.Phase;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
        target.PollStartedAtUtc = source.PollStartedAtUtc;
        target.PollCompletedAtUtc = source.PollCompletedAtUtc;
        target.RetryCount = source.RetryCount;
        target.LastError = source.LastError;
        target.ActiveWarId = source.ActiveWarId;
        target.StaleAfterUtc = source.StaleAfterUtc;
        target.PollIntervalSeconds = source.PollIntervalSeconds;
        target.FailureBackoffSeconds = source.FailureBackoffSeconds;
    }
}
