using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarReplayTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KnownAt_never_exposes_future_information()
    {
        var timeline = new[]
        {
            Obs(0, 10, 5),
            Obs(60, 20, 10),
            Obs(120, 900, 10, knownDelaySeconds: 600),
        };

        var known = WarReplay.KnownAt(timeline, Start.AddMinutes(3));
        var prediction = WarReplay.PredictLinearBaseline(timeline, Start.AddMinutes(3));

        Assert.Equal(2, known.Count);
        Assert.Equal(2, prediction.KnownObservationCount);
        Assert.Equal(10d / 60d, prediction.FactionScorePerSecond, 8);
    }

    [Fact]
    public void Replay_rejects_reordered_or_impossible_timeline_instead_of_silently_sorting()
    {
        var reordered = new[] { Obs(60, 20, 10), Obs(0, 10, 5) };
        Assert.Throws<ArgumentException>(() => WarReplay.ValidateTimeline(reordered));

        var sampled = Start.AddMinutes(1);
        Assert.Throws<ArgumentException>(() => WarReplayObservation.Create(
            77,
            sampled,
            sampled.AddSeconds(-1),
            10,
            10,
            1,
            1,
            100));
    }

    [Fact]
    public void Decision_points_skip_poll_noise_but_keep_chain_resets_and_target_events()
    {
        var timeline = new[]
        {
            Obs(0, 0, 0, factionChain: 10),
            Obs(5, 1, 0, factionChain: 11),
            Obs(10, 2, 0, factionChain: 0),
            Obs(15, 3, 0, factionChain: 1),
            Obs(70, 20, 10, factionChain: 2),
            Obs(75, 100, 10, factionChain: 3),
        };

        var selected = WarReplay.SelectDecisionPoints(timeline, Start.AddMinutes(5), TimeSpan.FromMinutes(1));

        Assert.Equal(new[] { 0d, 10d, 70d, 75d }, selected.Select(x => (x.SampledAtUtc - Start).TotalSeconds));
    }

    [Fact]
    public void Linear_baseline_uses_only_same_known_inputs_and_projects_target_finish()
    {
        var timeline = new[]
        {
            Obs(0, 10, 10),
            Obs(60, 40, 20),
        };

        var prediction = WarReplay.PredictLinearBaseline(timeline, Start.AddMinutes(1));

        Assert.Equal(WarReplayWinner.Faction, prediction.PredictedWinner);
        Assert.Equal(0.5d, prediction.FactionScorePerSecond, 8);
        Assert.Equal(1d / 6d, prediction.OpponentScorePerSecond, 8);
        Assert.Equal(Start.AddMinutes(3), prediction.PredictedFactionFinishUtc);
        Assert.Equal(Start.AddMinutes(9), prediction.PredictedOpponentFinishUtc);
    }

    [Fact]
    public void Evaluation_always_reports_sample_count_coverage_accuracy_and_eta_error()
    {
        var timeline = new[]
        {
            Obs(0, 10, 5),
            Obs(60, 40, 10),
            Obs(120, 70, 15),
            Obs(180, 100, 20),
        };
        var replayCase = new WarReplayCase(
            77,
            Start,
            Start.AddMinutes(3),
            WarReplayWinner.Faction,
            timeline);

        var metrics = WarReplay.EvaluateBaseline(replayCase, TimeSpan.FromMinutes(1));

        Assert.Equal(3, metrics.DecisionPointCount);
        Assert.Equal(2, metrics.ScoredPredictionCount);
        Assert.Equal(2m / 3m, metrics.Coverage);
        Assert.Equal(1d, metrics.OutcomeAccuracy);
        Assert.NotNull(metrics.MeanAbsoluteFinishEtaErrorSeconds);
        Assert.True(metrics.MeanAbsoluteFinishEtaErrorSeconds >= 0d);
    }

    [Fact]
    public void Chronological_split_keeps_entire_wars_together()
    {
        var older = Case(10, Start);
        var newer = Case(20, Start.AddDays(2));
        var newest = Case(30, Start.AddDays(4));

        var split = WarReplay.SplitChronologically(new[] { newest, older, newer }, trainingWarCount: 2);

        Assert.Equal(new long[] { 10, 20 }, split.Training.Select(x => x.WarId));
        Assert.Equal(new long[] { 30 }, split.Evaluation.Select(x => x.WarId));
        Assert.Empty(split.Training.SelectMany(x => x.Observations).Select(x => x.WarId)
            .Intersect(split.Evaluation.SelectMany(x => x.Observations).Select(x => x.WarId)));
    }

    [Fact]
    public void Baseline_is_unknown_until_two_known_samples_exist()
    {
        var prediction = WarReplay.PredictLinearBaseline(new[] { Obs(0, 10, 10) }, Start);

        Assert.Equal(WarReplayWinner.Unknown, prediction.PredictedWinner);
        Assert.Equal(0d, prediction.FactionScorePerSecond);
        Assert.Null(prediction.PredictedWarFinishUtc);
    }

    private static WarReplayObservation Obs(
        int seconds,
        int factionScore,
        int opponentScore,
        int factionChain = 1,
        int opponentChain = 1,
        int knownDelaySeconds = 0)
    {
        var sampled = Start.AddSeconds(seconds);
        return WarReplayObservation.Create(
            77,
            sampled,
            sampled.AddSeconds(knownDelaySeconds),
            factionScore,
            opponentScore,
            factionChain,
            opponentChain,
            100);
    }

    private static WarReplayCase Case(long warId, DateTimeOffset startedAtUtc)
    {
        var observations = new[]
        {
            WarReplayObservation.Create(warId, startedAtUtc, startedAtUtc, 0, 0, 0, 0, 100),
            WarReplayObservation.Create(warId, startedAtUtc.AddMinutes(1), startedAtUtc.AddMinutes(1), 100, 50, 1, 1, 100),
        };
        return new WarReplayCase(
            warId,
            startedAtUtc,
            startedAtUtc.AddMinutes(1),
            WarReplayWinner.Faction,
            observations);
    }
}
