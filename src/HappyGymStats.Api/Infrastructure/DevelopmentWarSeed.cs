using HappyGymStats.Api.Hubs;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Api.Infrastructure;

internal static class DevelopmentWarSeed
{
    private const long WarId = 48377;
    private const long FactionId = 111;
    private const long OpponentFactionId = 222;

    public static async Task SeedAsync(HappyGymStatsDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var existingWarIds = await db.WarCurrent
            .Where(e => e.ScopeKey == WarHubBroadcaster.ScopeKey && e.WarId != null)
            .Select(e => e.WarId!.Value)
            .ToListAsync(ct);
        if (existingWarIds.Count > 0)
        {
            db.WarRosterSnapshots.RemoveRange(db.WarRosterSnapshots.Where(e => existingWarIds.Contains(e.WarId)));
            db.WarScoreSamples.RemoveRange(db.WarScoreSamples.Where(e => existingWarIds.Contains(e.WarId)));
        }

        db.WarCurrent.RemoveRange(db.WarCurrent.Where(e => e.ScopeKey == WarHubBroadcaster.ScopeKey));
        db.WarPollerHeartbeats.RemoveRange(db.WarPollerHeartbeats.Where(e => e.ScopeKey == WarHubBroadcaster.ScopeKey));
        await db.SaveChangesAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var capturedAt = now.AddSeconds(-30);

        db.WarCurrent.Add(new WarCurrentEntity
        {
            ScopeKey = WarHubBroadcaster.ScopeKey,
            WarId = WarId,
            FactionId = FactionId,
            FactionName = "Happy Gym",
            OpponentFactionId = OpponentFactionId,
            OpponentFactionName = "Chain Breakers",
            StartedAtUtc = now.AddHours(-1),
            EndsAtUtc = now.AddHours(2),
            IsLive = true,
            ObservedAtUtc = capturedAt,
        });

        db.WarRosterSnapshots.AddRange(
            new WarRosterSnapshotEntity
            {
                WarId = WarId,
                FactionId = FactionId,
                FactionName = "Happy Gym",
                MemberId = 1001,
                MemberName = "Ready Planner",
                Score = 64,
                Chain = 12,
                Attacks = 6,
                StatusState = "Okay",
                StatusUntilUtc = null,
                CapturedAtUtc = capturedAt,
            },
            new WarRosterSnapshotEntity
            {
                WarId = WarId,
                FactionId = FactionId,
                FactionName = "Happy Gym",
                MemberId = 1002,
                MemberName = "Hospital Watch",
                Score = 22,
                Chain = 3,
                Attacks = 2,
                StatusState = "Hospital",
                StatusUntilUtc = now.AddMinutes(18),
                CapturedAtUtc = capturedAt,
            },
            new WarRosterSnapshotEntity
            {
                WarId = WarId,
                FactionId = FactionId,
                FactionName = "Happy Gym",
                MemberId = 1003,
                MemberName = "Idle Striker",
                Score = 0,
                Chain = 0,
                Attacks = 0,
                StatusState = "Idle",
                StatusUntilUtc = null,
                CapturedAtUtc = capturedAt,
            },
            new WarRosterSnapshotEntity
            {
                WarId = WarId,
                FactionId = OpponentFactionId,
                FactionName = "Chain Breakers",
                MemberId = 2001,
                MemberName = "Open Target",
                Score = 40,
                Chain = 6,
                Attacks = 4,
                StatusState = "Okay",
                StatusUntilUtc = null,
                CapturedAtUtc = capturedAt,
            },
            new WarRosterSnapshotEntity
            {
                WarId = WarId,
                FactionId = OpponentFactionId,
                FactionName = "Chain Breakers",
                MemberId = 2002,
                MemberName = "Opponent Hospital",
                Score = 12,
                Chain = 1,
                Attacks = 1,
                StatusState = "Hospital",
                StatusUntilUtc = now.AddMinutes(9),
                CapturedAtUtc = capturedAt,
            });

        db.WarScoreSamples.AddRange(
            new WarScoreSampleEntity
            {
                WarId = WarId,
                FactionId = FactionId,
                FactionName = "Happy Gym",
                FactionScore = 120,
                FactionChain = 14,
                OpponentFactionId = OpponentFactionId,
                OpponentFactionName = "Chain Breakers",
                OpponentScore = 90,
                OpponentChain = 8,
                SampledAtUtc = now.AddMinutes(-10),
            },
            new WarScoreSampleEntity
            {
                WarId = WarId,
                FactionId = FactionId,
                FactionName = "Happy Gym",
                FactionScore = 150,
                FactionChain = 21,
                OpponentFactionId = OpponentFactionId,
                OpponentFactionName = "Chain Breakers",
                OpponentScore = 102,
                OpponentChain = 11,
                SampledAtUtc = capturedAt,
            });

        db.WarPollerHeartbeats.Add(new WarPollerHeartbeatEntity
        {
            ScopeKey = WarHubBroadcaster.ScopeKey,
            Phase = "completed",
            UpdatedAtUtc = capturedAt,
            PollStartedAtUtc = now.AddMinutes(-1),
            PollCompletedAtUtc = capturedAt,
            RetryCount = 0,
            LastError = null,
            ActiveWarId = WarId,
            StaleAfterUtc = now.AddMinutes(30),
            PollIntervalSeconds = 60,
            FailureBackoffSeconds = 30,
        });

        await db.SaveChangesAsync(ct);
        logger.LogWarning("Seeded development war fixture {WarId}. Development authentication bypass must never handle production traffic.", WarId);
    }
}
