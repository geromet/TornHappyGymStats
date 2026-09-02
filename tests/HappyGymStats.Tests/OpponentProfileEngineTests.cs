using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class OpponentProfileEngineTests
{
    private const long ScoutedFactionId = 222;

    [Fact]
    public void BuildProfile_reconstructs_the_DerDoruk_war_48377_lump_case_and_stops_it_dominating_the_ranking()
    {
        // war 48377 correction (data/V2/reference/data-layer.md): DerDoruk looked like the best
        // targeter at ~23.9 score/attack, but subtract the 1000-chain milestone bonus of 640 from
        // his 955 and it is 315 over 40 attacks = 7.875/attack, dead on the faction median. He
        // landed one well-timed crossing hit; he did not out-target anyone.
        //
        // Fixture reconstructs that at integer precision: filler rows give a faction median of
        // exactly 7.8750 score/attack, and DerDoruk's residual 955 - 40*7.875 = 640.0 matches the
        // 640 bonus exactly.
        var wars = CreateWars(warIds: [1, 2, 3, 4]);

        // Six filler members, constant rate each, spanning the median: 6.25 / 7.0 / 7.875 / 7.875 /
        // 8.75 / 9.5 score per attack (score/attacks per war).
        var members = new List<RankedWarReportMemberEntity>();
        members.AddRange(FillerMemberAcrossWars(memberId: 8001, name: "Filler 6.25", warIds: [1, 2, 3, 4], score: 50, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8002, name: "Filler 7.0", warIds: [1, 2, 3, 4], score: 56, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8003, name: "Filler 7.875 a", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8004, name: "Filler 7.875 b", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8005, name: "Filler 8.75", warIds: [1, 2, 3, 4], score: 70, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8006, name: "Filler 9.5", warIds: [1, 2, 3, 4], score: 76, attacks: 8));

        const long derDorukId = 9999;
        members.Add(Member(warId: 4, memberId: derDorukId, name: "DerDoruk", score: 955, attacks: 40));

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "DEATH WATCH", wars, members);
        var derDoruk = Assert.Single(profile.Members, m => m.MemberId == derDorukId);

        // The detector's baseline emerges from the data, it is not hard-coded.
        Assert.Equal(7.8750m, profile.MedianScorePerAttack);

        // His raw rate still shows the ~3x distortion (kept, because the lump war did happen)...
        Assert.Equal(23.88m, derDoruk.AverageScorePerAttack);
        Assert.True(derDoruk.AverageScorePerAttack > profile.MedianScorePerAttack * 2.5m);

        // ...but the lump war is detected and its 640 bonus removed, landing him on the median.
        Assert.Equal(1, derDoruk.LumpWarCount);
        Assert.Equal(7.88m, derDoruk.LumpAdjustedScorePerAttack);
        Assert.True(Math.Abs(derDoruk.LumpAdjustedScorePerAttack - profile.MedianScorePerAttack) <= 0.05m);

        // His only war is lump-flagged, so the lump-adjusted per-war median falls back to the raw one.
        Assert.Equal(955m, derDoruk.RawMedianScorePerWar);
        Assert.Equal(derDoruk.RawMedianScorePerWar, derDoruk.LumpAdjustedScorePerWar);

        // Ranking is by lump-adjusted score/attack, so a genuine consistent swinger tops the table,
        // not the one-crossing-hit member - and the real spread (9.5 top) is nothing like the raw
        // outlier suggested.
        Assert.NotEqual(derDorukId, profile.Members[0].MemberId);
        Assert.Equal(9.50m, profile.Members[0].LumpAdjustedScorePerAttack);
        Assert.True(profile.Members[0].LumpAdjustedScorePerAttack < derDoruk.AverageScorePerAttack);
    }

    [Fact]
    public void BuildProfile_does_not_flag_a_big_war_whose_score_is_not_chain_bonus_shaped()
    {
        var wars = CreateWars(warIds: [1, 2, 3, 4]);

        var members = new List<RankedWarReportMemberEntity>();
        members.AddRange(FillerMemberAcrossWars(memberId: 8001, name: "Filler a", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8002, name: "Filler b", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8003, name: "Filler c", warIds: [1, 2, 3, 4], score: 63, attacks: 8));

        // Three quiet wars, then one genuinely huge war - 1600 over 100 attacks. Residual
        // 1600 - 100*7.875 = 812.5 sits between the 640 and 1280 bonuses, close to neither.
        const long grinderId = 7001;
        members.Add(Member(warId: 1, memberId: grinderId, name: "Grinder", score: 63, attacks: 8));
        members.Add(Member(warId: 2, memberId: grinderId, name: "Grinder", score: 63, attacks: 8));
        members.Add(Member(warId: 3, memberId: grinderId, name: "Grinder", score: 63, attacks: 8));
        members.Add(Member(warId: 4, memberId: grinderId, name: "Grinder", score: 1600, attacks: 100));

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Grind House", wars, members);
        var grinder = Assert.Single(profile.Members, m => m.MemberId == grinderId);

        Assert.Equal(0, grinder.LumpWarCount);
        // No war dropped, so the lump-adjusted per-war median equals the raw one. (Median
        // score/attack legitimately differs from the weighted-mean AverageScorePerAttack here -
        // the 1600 war is a genuine outlier; that's the point of using a median.)
        Assert.Equal(grinder.RawMedianScorePerWar, grinder.LumpAdjustedScorePerWar);
        Assert.Equal(4, grinder.WarsParticipated);
    }

    [Fact]
    public void BuildProfile_does_not_flag_a_strong_above_median_member_with_no_lump()
    {
        var wars = CreateWars(warIds: [1, 2, 3, 4]);

        var members = new List<RankedWarReportMemberEntity>();
        members.AddRange(FillerMemberAcrossWars(memberId: 8001, name: "Filler a", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8002, name: "Filler b", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8003, name: "Filler c", warIds: [1, 2, 3, 4], score: 63, attacks: 8));

        // Consistently 11.0 score/attack, well above the 7.875 median, no bonus anywhere. Per-war
        // residual 440 - 40*7.875 = 125 - nearest detectable bonus is 160, and 125 is outside its
        // tolerance band. A strong member's best war must not be discarded as a lump.
        const long strongId = 7002;
        members.Add(Member(warId: 1, memberId: strongId, name: "Strong", score: 440, attacks: 40));
        members.Add(Member(warId: 2, memberId: strongId, name: "Strong", score: 440, attacks: 40));
        members.Add(Member(warId: 3, memberId: strongId, name: "Strong", score: 440, attacks: 40));
        members.Add(Member(warId: 4, memberId: strongId, name: "Strong", score: 440, attacks: 40));

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Strong Arm", wars, members);
        var strong = Assert.Single(profile.Members, m => m.MemberId == strongId);

        Assert.Equal(0, strong.LumpWarCount);
        Assert.Equal(11.00m, strong.LumpAdjustedScorePerAttack);
        Assert.Equal(strong.AverageScorePerAttack, strong.LumpAdjustedScorePerAttack);
        // Genuinely the top threat, and stays there.
        Assert.Equal(strongId, profile.Members[0].MemberId);
    }

    [Fact]
    public void BuildProfile_flags_a_lump_on_a_member_who_also_hits_above_the_faction_median()
    {
        var wars = CreateWars(warIds: [1, 2, 3, 4]);

        var members = new List<RankedWarReportMemberEntity>();
        members.AddRange(FillerMemberAcrossWars(memberId: 8001, name: "Filler a", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8002, name: "Filler b", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8003, name: "Filler c", warIds: [1, 2, 3, 4], score: 63, attacks: 8));

        // True rate ~9/attack (above the 7.875 median), plus one war carrying a 640 lump:
        // 1000 over 40 attacks, residual 1000 - 40*7.875 = 685, within tolerance of 640. The
        // above-median drift must not hide the lump.
        const long starId = 7003;
        members.Add(Member(warId: 1, memberId: starId, name: "LumpyStar", score: 80, attacks: 10));
        members.Add(Member(warId: 2, memberId: starId, name: "LumpyStar", score: 90, attacks: 10));
        members.Add(Member(warId: 3, memberId: starId, name: "LumpyStar", score: 100, attacks: 10));
        members.Add(Member(warId: 4, memberId: starId, name: "LumpyStar", score: 1000, attacks: 40));

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Star Chamber", wars, members);
        var star = Assert.Single(profile.Members, m => m.MemberId == starId);

        Assert.Equal(1, star.LumpWarCount);
        // (1270 total score - 640 bonus) / 70 attacks = 9.0 - his real rate recovered.
        Assert.Equal(9.00m, star.LumpAdjustedScorePerAttack);
        // Raw rate is inflated by the lump.
        Assert.Equal(18.14m, star.AverageScorePerAttack);
        // "Show both": the lump war (1000) is dropped from the adjusted per-war median but kept in
        // the raw one, so the two differ.
        Assert.Equal(90m, star.LumpAdjustedScorePerWar);
        Assert.Equal(95m, star.RawMedianScorePerWar);
        Assert.Equal(1000, star.MaxScoreInAWar);
    }

    [Fact]
    public void BuildProfile_lump_adjusted_score_per_attack_is_a_median_of_per_war_rates_not_a_weighted_mean()
    {
        var wars = CreateWars(warIds: [1, 2, 3, 4]);

        var members = new List<RankedWarReportMemberEntity>();
        members.AddRange(FillerMemberAcrossWars(memberId: 8001, name: "Filler a", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8002, name: "Filler b", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8003, name: "Filler c", warIds: [1, 2, 3, 4], score: 63, attacks: 8));

        // Per-war rates [5, 5, 5, 30] with unequal attack counts. Median of the rates is 5;
        // the attack-weighted mean would be 375 / 25 = 15. No war is bonus-shaped
        // (the 300-score war's residual 300 - 10*7.875 = 221.25 matches no bonus), so the
        // difference is purely median-vs-mean.
        const long spikyId = 7005;
        members.Add(Member(warId: 1, memberId: spikyId, name: "Spiky", score: 25, attacks: 5));
        members.Add(Member(warId: 2, memberId: spikyId, name: "Spiky", score: 25, attacks: 5));
        members.Add(Member(warId: 3, memberId: spikyId, name: "Spiky", score: 25, attacks: 5));
        members.Add(Member(warId: 4, memberId: spikyId, name: "Spiky", score: 300, attacks: 10));

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Spike Squad", wars, members);
        var spiky = Assert.Single(profile.Members, m => m.MemberId == spikyId);

        Assert.Equal(0, spiky.LumpWarCount);
        Assert.Equal(5.00m, spiky.LumpAdjustedScorePerAttack);
        Assert.Equal(15.00m, spiky.AverageScorePerAttack);
    }

    [Fact]
    public void BuildProfile_falls_back_to_the_raw_per_war_median_when_every_war_is_lump_flagged()
    {
        var wars = CreateWars(warIds: [1, 2, 3, 4]);

        var members = new List<RankedWarReportMemberEntity>();
        members.AddRange(FillerMemberAcrossWars(memberId: 8001, name: "Filler a", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8002, name: "Filler b", warIds: [1, 2, 3, 4], score: 63, attacks: 8));
        members.AddRange(FillerMemberAcrossWars(memberId: 8003, name: "Filler c", warIds: [1, 2, 3, 4], score: 63, attacks: 8));

        // Both of this member's wars are bonus-shaped: 318 over 20 attacks, residual
        // 318 - 20*7.875 = 160.5, within tolerance of the 160 bonus.
        const long chaserId = 7004;
        members.Add(Member(warId: 1, memberId: chaserId, name: "MilestoneChaser", score: 318, attacks: 20));
        members.Add(Member(warId: 2, memberId: chaserId, name: "MilestoneChaser", score: 318, attacks: 20));

        var profile = OpponentProfileEngine.BuildProfile(ScoutedFactionId, "Chasers", wars, members);
        var chaser = Assert.Single(profile.Members, m => m.MemberId == chaserId);

        Assert.Equal(2, chaser.LumpWarCount);
        Assert.Equal(318m, chaser.RawMedianScorePerWar);
        Assert.Equal(chaser.RawMedianScorePerWar, chaser.LumpAdjustedScorePerWar);
        // (636 total - 320 bonuses) / 40 attacks = 7.9
        Assert.Equal(7.90m, chaser.LumpAdjustedScorePerAttack);
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
        Assert.Equal(0, hitter.LumpWarCount);
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
        Assert.Equal(0m, profile.MedianScorePerAttack);
        Assert.Empty(profile.Members);
    }

    private static IEnumerable<RankedWarReportMemberEntity> FillerMemberAcrossWars(
        long memberId, string name, IEnumerable<long> warIds, int score, int attacks)
        => warIds.Select(warId => Member(warId, memberId, name, score, attacks));

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
