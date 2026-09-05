using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarStateDerivationPolicyRegressionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_731_001_800);

    [Fact]
    public void Equal_timestamp_score_samples_keep_invalid_window_diagnostic()
    {
        var roster = new[]
        {
            Roster(1, 10, "okay"),
            Roster(2, 20, "okay"),
        };
        var samples = new[]
        {
            Sample(1, factionScore: 90, opponentScore: 80, Now),
            Sample(2, factionScore: 100, opponentScore: 90, Now),
        };

        var state = new WarStateDerivationEngine().Derive(roster, samples, Now);

        Assert.All(state.Factions, faction =>
        {
            Assert.False(faction.ScoreRate.IsAvailable);
            Assert.Equal("invalid-score-window", faction.ScoreRate.Diagnostic);
            Assert.Equal(0, faction.ScoreRate.WindowSeconds);
            Assert.False(faction.Eta.IsAvailable);
            Assert.Equal("invalid-score-window", faction.Eta.Diagnostic);
        });
        Assert.Equal(2, state.Warnings.Count(warning => warning.Contains("non-positive time window", StringComparison.Ordinal)));
    }

    [Fact]
    public void Reaching_winning_score_keeps_zero_eta_and_zero_attacks_to_finish_available()
    {
        var roster = new[]
        {
            Roster(1, 10, "okay", score: 100, attacks: 10),
            Roster(2, 20, "okay", score: 50, attacks: 5),
        };
        var samples = new[]
        {
            Sample(1, factionScore: 90, opponentScore: 40, Now.AddMinutes(-1)),
            Sample(2, factionScore: 100, opponentScore: 50, Now),
        };

        var state = new WarStateDerivationEngine(winningScore: 100).Derive(roster, samples, Now);
        var faction = state.Factions.Single(candidate => candidate.FactionId == 1);

        Assert.Equal(0, faction.RemainingScoreToWin);
        Assert.True(faction.Eta.IsAvailable);
        Assert.Equal(0, faction.Eta.SecondsUntilWin);
        Assert.True(faction.AttacksToFinish.IsAvailable);
        Assert.Equal(0, faction.AttacksToFinish.RequiredAttacks);
        Assert.Equal(10m, faction.AttacksToFinish.AverageScorePerAttack);
    }

    [Fact]
    public void Unknown_member_status_remains_unknown_and_counts_as_unavailable()
    {
        var roster = new[]
        {
            Roster(1, 10, "mystery-state"),
            Roster(2, 20, "okay"),
        };

        var state = new WarStateDerivationEngine().Derive(roster, [], Now);
        var faction = state.Factions.Single(candidate => candidate.FactionId == 1);
        var member = Assert.Single(faction.Members);

        Assert.Equal(WarMemberAvailabilityKind.Unknown, member.Availability);
        Assert.Equal(0, faction.AvailableMemberCount);
        Assert.Equal(1, faction.UnavailableMemberCount);
        Assert.Equal(1m, faction.CoverageRatio);
        Assert.DoesNotContain(state.Holes, hole => hole.FactionId == 2 && hole.MemberId == 10 && hole.Kind == WarHoleKind.OpenTarget);
    }

    private static WarRosterSnapshotEntity Roster(
        long factionId,
        long memberId,
        string status,
        int score = 10,
        int attacks = 1)
        => new()
        {
            WarId = 48_377,
            FactionId = factionId,
            FactionName = factionId == 1 ? "Alpha" : "Bravo",
            MemberId = memberId,
            MemberName = $"Member {memberId}",
            Score = score,
            Chain = 0,
            Attacks = attacks,
            StatusState = status,
            CapturedAtUtc = Now,
        };

    private static WarScoreSampleEntity Sample(
        long id,
        int factionScore,
        int opponentScore,
        DateTimeOffset sampledAtUtc)
        => new()
        {
            Id = id,
            WarId = 48_377,
            FactionId = 1,
            FactionName = "Alpha",
            FactionScore = factionScore,
            FactionChain = 10,
            OpponentFactionId = 2,
            OpponentFactionName = "Bravo",
            OpponentScore = opponentScore,
            OpponentChain = 8,
            SampledAtUtc = sampledAtUtc,
        };
}
