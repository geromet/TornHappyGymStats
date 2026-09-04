using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarObjectiveTests
{
    [Fact]
    public void CreateDefault_uses_safe_competitive_non_explicit_objective()
    {
        var createdAt = new DateTimeOffset(2026, 9, 4, 20, 0, 0, TimeSpan.FromHours(2));

        var objective = WarObjectiveVersion.CreateDefault(48377, createdAt);

        Assert.Equal(48377, objective.WarId);
        Assert.Equal(1, objective.Version);
        Assert.Equal(WarObjectiveMode.CompetitiveWin, objective.Mode);
        Assert.False(objective.IsExplicit);
        Assert.Null(objective.StopAtFactionScore);
        Assert.Equal("system", objective.ChangedBy);
        Assert.Equal(createdAt.ToUniversalTime(), objective.CreatedAtUtc);
    }

    [Theory]
    [InlineData(WarObjectiveMode.TermedWin)]
    [InlineData(WarObjectiveMode.TermedLoss)]
    public void CreateNext_creates_new_explicit_version_without_mutating_previous(WarObjectiveMode mode)
    {
        var original = WarObjectiveVersion.CreateDefault(48377, DateTimeOffset.UnixEpoch);

        var next = original.CreateNext(
            mode,
            changedBy: "planner-42",
            createdAtUtc: DateTimeOffset.UnixEpoch.AddHours(1),
            stopAtFactionScore: 1250,
            notes: " agreed terms ");

        Assert.Equal(1, original.Version);
        Assert.False(original.IsExplicit);
        Assert.Null(original.StopAtFactionScore);

        Assert.Equal(2, next.Version);
        Assert.True(next.IsExplicit);
        Assert.Equal(mode, next.Mode);
        Assert.Equal(1250, next.StopAtFactionScore);
        Assert.Equal("agreed terms", next.Notes);
        Assert.Equal("planner-42", next.ChangedBy);
    }

    [Fact]
    public void Evaluate_suppresses_recommendations_only_when_explicit_stop_score_is_reached()
    {
        var objective = WarObjectiveVersion
            .CreateDefault(48377, DateTimeOffset.UnixEpoch)
            .CreateNext(
                WarObjectiveMode.TermedWin,
                changedBy: "planner",
                createdAtUtc: DateTimeOffset.UnixEpoch.AddMinutes(1),
                stopAtFactionScore: 1000);

        var before = WarObjectiveEvaluator.Evaluate(objective, 999);
        var reached = WarObjectiveEvaluator.Evaluate(objective, 1000);
        var beyond = WarObjectiveEvaluator.Evaluate(objective, 1001);

        Assert.True(before.RecommendationsAllowed);
        Assert.Null(before.StopReason);

        Assert.False(reached.RecommendationsAllowed);
        Assert.Contains("1000", reached.StopReason, StringComparison.Ordinal);
        Assert.False(beyond.RecommendationsAllowed);
    }

    [Fact]
    public void Evaluate_default_competitive_objective_does_not_invent_a_stop_condition()
    {
        var objective = WarObjectiveVersion.CreateDefault(48377, DateTimeOffset.UnixEpoch);

        var evaluation = WarObjectiveEvaluator.Evaluate(objective, int.MaxValue);

        Assert.True(evaluation.RecommendationsAllowed);
        Assert.Null(evaluation.StopReason);
    }

    [Fact]
    public void Invalid_objective_inputs_are_rejected_at_creation_seam()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WarObjectiveVersion.CreateDefault(0, DateTimeOffset.UnixEpoch));

        var objective = WarObjectiveVersion.CreateDefault(48377, DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            objective.CreateNext(
                WarObjectiveMode.TermedWin,
                changedBy: "planner",
                createdAtUtc: DateTimeOffset.UnixEpoch,
                stopAtFactionScore: -1));

        Assert.Throws<ArgumentException>(() =>
            objective.CreateNext(
                WarObjectiveMode.TermedWin,
                changedBy: " ",
                createdAtUtc: DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Evaluator_rejects_negative_current_score()
    {
        var objective = WarObjectiveVersion.CreateDefault(48377, DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WarObjectiveEvaluator.Evaluate(objective, -1));
    }
}
