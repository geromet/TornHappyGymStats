using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class OpponentProfileEngineTests
{
    private const long ScoutedFactionId = 222;

    [Fact]
    public void BuildProfile_uses_median_score_per_war_so_a_single_lump_war_does_not_dominate_ranking()
    {
        var wars = CreateWars(warIds: [1, 2, 3, 4]);

        // Member A: consistently ~100/war across 4 wars (median 100).
        // Member B: 3 quiet wars near 20, plus one huge chain-milestone lump war at 900 (median 20).
        var members = new[]
        {
            Member(1, 9001, "Steady Alice", score: 95, attacks: 10),
            Member(2, 9001, "Steady Alice", score: 105, attacks: 10),
            Member(3, 9001, "Steady Alice", score: 100, attacks: 10),
            Member(4, 9001, "Steady Alice", score: 100, attacks: 10),

            Member(1, 9002, "Lumpy Bob", score: 20, attacks: 5),
            Member(2, 9002, "Lumpy Bob", score: 18, attacks: 5),
            Member(3, 9002, "Lumpy Bob", score: 900, attacks: 5),
            Member(4, 9002, "Lumpy Bob", score: 22, attacks: 5),
        };

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Chain Breakers", wars, members);

        var alice = Assert.Single(profile.Members, m => m.MemberId == 9001);
        var bob = Assert.Single(profile.Members, m => m.MemberId == 9002);

        Assert.Equal(100m, alice.LumpAdjustedScorePerWar);
        Assert.Equal(21m, bob.LumpAdjustedScorePerWar);

        // Median-based ranking correctly ranks the consistent swinger above the one-lump-war member,
        // even though Bob's raw total (960) exceeds Alice's (400).
        Assert.True(profile.Members[0].MemberId == 9001, "the consistent swinger should rank above the one-off lump-war member");
        Assert.Equal(960, bob.TotalScore);
        Assert.Equal(400, alice.TotalScore);
    }

    [Fact]
    public void BuildProfile_classifies_members_by_idle_rate_and_participation_rate()
    {
        var wars = CreateWars(warIds: [1, 2, 3, 4, 5]);

        var members = new[]
        {
            // Idle in 3/4 observed wars -> idle-prone regardless of participation.
            Member(1, 1001, "Ghost", score: 0, attacks: 0, isIdle: true),
            Member(2, 1001, "Ghost", score: 0, attacks: 0, isIdle: true),
            Member(3, 1001, "Ghost", score: 40, attacks: 3, isIdle: false),
            Member(4, 1001, "Ghost", score: 0, attacks: 0, isIdle: true),

            // Participates in 4/5 wars, never idle -> consistent swinger.
            Member(1, 1002, "Regular", score: 50, attacks: 5),
            Member(2, 1002, "Regular", score: 55, attacks: 5),
            Member(3, 1002, "Regular", score: 45, attacks: 5),
            Member(4, 1002, "Regular", score: 60, attacks: 5),

            // Participates in only 1/5 wars -> occasional swinger.
            Member(5, 1003, "OneTimer", score: 30, attacks: 3),
        };

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Chain Breakers", wars, members);

        Assert.Equal(OpponentThreatTier.IdleProne, profile.Members.Single(m => m.MemberId == 1001).ThreatTier);
        Assert.Equal(OpponentThreatTier.ConsistentSwinger, profile.Members.Single(m => m.MemberId == 1002).ThreatTier);
        Assert.Equal(OpponentThreatTier.OccasionalSwinger, profile.Members.Single(m => m.MemberId == 1003).ThreatTier);

        Assert.Equal(2, profile.ActiveMemberCount);
        Assert.Equal(1, profile.IdleProneMemberCount);
        Assert.Equal(5, profile.TotalWarsObserved);
    }

    [Fact]
    public void BuildProfile_computes_average_score_per_attack_and_score_range()
    {
        var wars = CreateWars(warIds: [1, 2]);
        var members = new[]
        {
            Member(1, 5001, "Hitter", score: 100, attacks: 10),
            Member(2, 5001, "Hitter", score: 50, attacks: 5),
        };

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Chain Breakers", wars, members);
        var hitter = Assert.Single(profile.Members);

        Assert.Equal(150, hitter.TotalScore);
        Assert.Equal(15, hitter.TotalAttacks);
        Assert.Equal(10m, hitter.AverageScorePerAttack);
        Assert.Equal(50, hitter.MinScoreInAWar);
        Assert.Equal(100, hitter.MaxScoreInAWar);
        Assert.Equal(1m, hitter.ParticipationRate);
    }

    [Fact]
    public void BuildProfile_rejects_non_positive_faction_ids()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpponentProfileEngine.BuildProfile(0, "n/a", [], []));
    }

    [Fact]
    public void BuildProfile_returns_empty_roster_for_a_faction_with_no_wars()
    {
        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Chain Breakers", [], []);

        Assert.Equal(0, profile.TotalWarsObserved);
        Assert.Null(profile.EarliestWarStartedAtUtc);
        Assert.Null(profile.LatestWarStartedAtUtc);
        Assert.Empty(profile.Members);
    }

    private static IReadOnlyList<RankedWarHistoryEntity> CreateWars(IEnumerable<long> warIds)
        => warIds.Select(id => new RankedWarHistoryEntity
        {
            WarId = id,
            FactionId = ScoutedFactionId,
            FactionName = "Chain Breakers",
            OpponentFactionId = 111,
            OpponentFactionName = "Happy Gym",
            StartedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(id),
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IngestedAtUtc = DateTimeOffset.UtcNow,
            ReportCapturedAtUtc = DateTimeOffset.UtcNow,
            ReportIngestedAtUtc = DateTimeOffset.UtcNow,
        }).ToArray();

    private static RankedWarReportMemberEntity Member(
        long warId,
        long memberId,
        string name,
        int score,
        int attacks,
        bool isIdle = false)
        => new()
        {
            WarId = warId,
            FactionId = ScoutedFactionId,
            FactionName = "Chain Breakers",
            MemberId = memberId,
            MemberName = name,
            Score = score,
            Chain = 0,
            Attacks = attacks,
            IsIdleAttacker = isIdle,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IngestedAtUtc = DateTimeOffset.UtcNow,
        };
}
