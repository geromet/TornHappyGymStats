using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Api.Infrastructure;

/// <summary>
/// Development-only ranked-war history used to render the evidence-first Scout page end to end.
/// The fixture is deterministic and local to the development SQLite database; it never calls Torn.
/// Activation is explicit so ordinary development-auth hosts and tests do not receive Scout data.
/// </summary>
internal static class DevelopmentScoutSeed
{
    public const long ScoutedFactionId = 222;
    private const string ScopeKey = "dev-scout-render";
    private const string EnableVariable = "HAPPYGYMSTATS_DEV_SEED_SCOUT";
    private static readonly long[] WarIds = [91001, 91002, 91003, 91004, 91005, 91006];

    public static async Task SeedAsync(HappyGymStatsDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var enabledRaw = Environment.GetEnvironmentVariable(EnableVariable);
        var enabled = string.Equals(enabledRaw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return;
        }

        db.RankedWarReportMembers.RemoveRange(
            db.RankedWarReportMembers.Where(member => WarIds.Contains(member.WarId)));
        db.RankedWarHistory.RemoveRange(
            db.RankedWarHistory.Where(war => WarIds.Contains(war.WarId)));
        db.RankedWarHistoryBackfillState.RemoveRange(
            db.RankedWarHistoryBackfillState.Where(state => state.ScopeKey == ScopeKey));
        await db.SaveChangesAsync(ct);

        var capturedAt = new DateTimeOffset(2026, 9, 5, 10, 30, 0, TimeSpan.Zero);
        for (var index = 0; index < WarIds.Length; index++)
        {
            var warId = WarIds[index];
            var startedAt = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero).AddDays(index * 7);
            var scoutedWasFaction = index % 2 == 0;
            var factionId = scoutedWasFaction ? ScoutedFactionId : 333L;
            var opponentFactionId = scoutedWasFaction ? 333L : ScoutedFactionId;
            var factionScore = scoutedWasFaction ? 5200 + (index * 220) : 4300 + (index * 170);
            var opponentScore = scoutedWasFaction ? 4100 + (index * 150) : 5600 + (index * 210);
            var winnerFactionId = factionScore >= opponentScore ? factionId : opponentFactionId;

            db.RankedWarHistory.Add(new RankedWarHistoryEntity
            {
                WarId = warId,
                FactionId = factionId,
                FactionName = scoutedWasFaction ? "Chain Breakers" : "Weekly Rivals",
                OpponentFactionId = opponentFactionId,
                OpponentFactionName = scoutedWasFaction ? "Weekly Rivals" : "Chain Breakers",
                StartedAtUtc = startedAt,
                EndedAtUtc = startedAt.AddHours(5 + (index % 3)),
                WinnerFactionId = winnerFactionId,
                FactionScore = factionScore,
                OpponentScore = opponentScore,
                CapturedAtUtc = capturedAt.AddMinutes(index),
                IngestedAtUtc = capturedAt.AddMinutes(index),
                ReportCapturedAtUtc = capturedAt.AddMinutes(index),
                ReportIngestedAtUtc = capturedAt.AddMinutes(index),
            });

            AddMember(db, warId, 2201, "Heavy Hitter", 1750 + (index * 90), 32 + index, capturedAt);
            AddMember(db, warId, 2202, "Steady Hand", 1120 + (index * 45), 26 + index, capturedAt);
            AddMember(db, warId, 2203, "Burst Player", 760 + (index * 140), 18 + index, capturedAt);
            AddMember(db, warId, 2204, "Occasional", 260 + (index * 20), 7 + (index % 3), capturedAt);
        }

        db.RankedWarHistoryBackfillState.Add(new RankedWarHistoryBackfillStateEntity
        {
            ScopeKey = ScopeKey,
            Status = RankedWarHistoryBackfillStatus.Completed,
            Phase = RankedWarHistoryBackfillPhase.Idle,
            NextHistoryPageUrl = null,
            LastProcessedWarId = WarIds[^1],
            PagesProcessed = 9,
            ReportsProcessed = WarIds.Length,
            RetryCount = 0,
            LastFailureCategory = null,
            LastErrorMessage = null,
            LastSuccessAtUtc = capturedAt,
            LastFailureAtUtc = null,
            NextRetryAtUtc = null,
            CreatedAtUtc = capturedAt.AddHours(-2),
            UpdatedAtUtc = capturedAt,
        });

        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Seeded development Scout fixture for faction {FactionId} with {WarCount} captured wars. Development authentication bypass must never handle production traffic.",
            ScoutedFactionId,
            WarIds.Length);
    }

    private static void AddMember(
        HappyGymStatsDbContext db,
        long warId,
        long memberId,
        string memberName,
        int score,
        int attacks,
        DateTimeOffset capturedAt)
    {
        db.RankedWarReportMembers.Add(new RankedWarReportMemberEntity
        {
            WarId = warId,
            FactionId = ScoutedFactionId,
            FactionName = "Chain Breakers",
            MemberId = memberId,
            MemberName = memberName,
            Score = score,
            Attacks = attacks,
            CapturedAtUtc = capturedAt,
            IngestedAtUtc = capturedAt,
        });
    }
}
