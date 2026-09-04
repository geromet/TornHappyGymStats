using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Tests;

/// <summary>
/// Shared fixtures for the war-accounting test suites. All timestamps are fixed so ledgers built
/// from the same source rows are byte-for-byte reproducible.
/// </summary>
internal static class WarAccountingTestData
{
    public const long FactionId = 222;
    public const string FactionName = "DEATH WATCH";
    public const long OpponentFactionId = 333;
    public const string OpponentFactionName = "The Firm";

    public static DateTimeOffset WarEnd => new(2026, 1, 12, 20, 0, 0, TimeSpan.Zero);

    public static RankedWarHistoryEntity CompletedWar(long warId = 48377)
    {
        return new RankedWarHistoryEntity
        {
            WarId = warId,
            FactionId = FactionId,
            FactionName = FactionName,
            OpponentFactionId = OpponentFactionId,
            OpponentFactionName = OpponentFactionName,
            StartedAtUtc = new DateTimeOffset(2026, 1, 10, 20, 0, 0, TimeSpan.Zero),
            EndedAtUtc = WarEnd,
            WinnerFactionId = FactionId,
            FactionScore = 20000,
            FactionChain = 1000,
            OpponentScore = 15000,
            OpponentChain = 500,
            Status = "ended",
            CapturedAtUtc = WarEnd,
            IngestedAtUtc = WarEnd,
            ReportCapturedAtUtc = WarEnd,
            ReportIngestedAtUtc = WarEnd,
        };
    }

    public static RankedWarHistoryEntity UnfinishedWar(long warId = 48377)
    {
        var war = CompletedWar(warId);
        war.EndedAtUtc = null;
        war.WinnerFactionId = null;
        return war;
    }

    public static RankedWarReportMemberEntity ReportMember(
        long warId,
        long memberId,
        string name,
        int score,
        int attacks,
        long factionId = FactionId)
    {
        return new RankedWarReportMemberEntity
        {
            WarId = warId,
            FactionId = factionId,
            FactionName = FactionName,
            MemberId = memberId,
            MemberName = name,
            Score = score,
            Chain = 0,
            Attacks = attacks,
            StatusState = "Okay",
            StatusUntilUtc = null,
            IsIdleAttacker = false,
            CapturedAtUtc = WarEnd.AddHours(-1),
            IngestedAtUtc = WarEnd.AddHours(-1).AddMinutes(5),
        };
    }

    public static PayoutPolicy RespectPolicy(decimal respectRate = 1.0m, string version = "1.0")
    {
        return new PayoutPolicy(
            "Respect",
            version,
            new PayoutRateTable(
                RespectRatePerPoint: respectRate,
                WarHitRate: 0m,
                AssistRate: 0m,
                OutsideHitRate: 0m,
                ChainSaveRate: 0m,
                MilestoneBonusRate: 0m,
                PushWindowRate: 0m,
                RetaliationRate: 0m,
                EnergyRatePerPoint: 0m),
            MilestoneLumpHandling.IncludedInRespect,
            LeadershipCutRate: 0m);
    }

    public static PayoutPolicy HitPolicy(decimal warHitRate = 5m, string version = "1.0")
    {
        return new PayoutPolicy(
            "Per-hit",
            version,
            new PayoutRateTable(
                RespectRatePerPoint: 0m,
                WarHitRate: warHitRate,
                AssistRate: 0m,
                OutsideHitRate: 0m,
                ChainSaveRate: 0m,
                MilestoneBonusRate: 0m,
                PushWindowRate: 0m,
                RetaliationRate: 0m,
                EnergyRatePerPoint: 0m),
            MilestoneLumpHandling.IncludedInRespect,
            LeadershipCutRate: 0m);
    }
}
