namespace HappyGymStats.Core.War;

public enum WarReplayWinner
{
    Unknown = 0,
    Faction = 1,
    Opponent = 2,
}

/// <summary>
/// One immutable input available to a historical replay. <see cref="KnownAtUtc"/> is the
/// anti-leakage boundary: a decision made at time T may only consume observations whose
/// KnownAtUtc is at or before T.
/// </summary>
public sealed record WarReplayObservation(
    long WarId,
    DateTimeOffset SampledAtUtc,
    DateTimeOffset KnownAtUtc,
    int FactionScore,
    int OpponentScore,
    int FactionChain,
    int OpponentChain,
    int TargetScore)
{
    public static WarReplayObservation Create(
        long warId,
        DateTimeOffset sampledAtUtc,
        DateTimeOffset knownAtUtc,
        int factionScore,
        int opponentScore,
        int factionChain,
        int opponentChain,
        int targetScore)
    {
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        RequireUtc(sampledAtUtc, nameof(sampledAtUtc));
        RequireUtc(knownAtUtc, nameof(knownAtUtc));
        if (knownAtUtc < sampledAtUtc)
            throw new ArgumentException("An observation cannot be known before it was sampled.", nameof(knownAtUtc));
        if (factionScore < 0) throw new ArgumentOutOfRangeException(nameof(factionScore));
        if (opponentScore < 0) throw new ArgumentOutOfRangeException(nameof(opponentScore));
        if (factionChain < 0) throw new ArgumentOutOfRangeException(nameof(factionChain));
        if (opponentChain < 0) throw new ArgumentOutOfRangeException(nameof(opponentChain));
        if (targetScore <= 0) throw new ArgumentOutOfRangeException(nameof(targetScore));

        return new WarReplayObservation(
            warId,
            sampledAtUtc,
            knownAtUtc,
            factionScore,
            opponentScore,
            factionChain,
            opponentChain,
            targetScore);
    }

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Replay timestamps must be explicit UTC values.", parameterName);
    }
}

public sealed record WarReplayBaselinePrediction(
    long WarId,
    DateTimeOffset DecisionAtUtc,
    WarReplayWinner PredictedWinner,
    DateTimeOffset? PredictedFactionFinishUtc,
    DateTimeOffset? PredictedOpponentFinishUtc,
    double FactionScorePerSecond,
    double OpponentScorePerSecond,
    int KnownObservationCount)
{
    public DateTimeOffset? PredictedWarFinishUtc => PredictedWinner switch
    {
        WarReplayWinner.Faction => PredictedFactionFinishUtc,
        WarReplayWinner.Opponent => PredictedOpponentFinishUtc,
        _ => null,
    };
}

public sealed record WarReplayMetrics(
    int DecisionPointCount,
    int ScoredPredictionCount,
    decimal Coverage,
    double? OutcomeAccuracy,
    double? MeanAbsoluteFinishEtaErrorSeconds);

public sealed record WarReplayCase(
    long WarId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    WarReplayWinner ActualWinner,
    IReadOnlyList<WarReplayObservation> Observations);

public sealed record WarReplayChronologicalSplit(
    IReadOnlyList<WarReplayCase> Training,
    IReadOnlyList<WarReplayCase> Evaluation);

/// <summary>
/// Deterministic Phase-A replay helpers for M013 evaluation. This deliberately contains only a
/// naive linear score-rate baseline and evaluation plumbing; it is not an advanced strategy model.
/// </summary>
public static class WarReplay
{
    public static IReadOnlyList<WarReplayObservation> KnownAt(
        IReadOnlyList<WarReplayObservation> observations,
        DateTimeOffset decisionAtUtc)
    {
        ValidateTimeline(observations);
        RequireUtc(decisionAtUtc, nameof(decisionAtUtc));

        return observations
            .TakeWhile(observation => observation.KnownAtUtc <= decisionAtUtc)
            .ToArray();
    }

    /// <summary>
    /// Samples stable decision points instead of every poll row. The first known point is kept;
    /// subsequent points are kept after the configured spacing, on a chain reset, or when either
    /// side reaches the then-known target score. No future row is inspected to decide whether a
    /// current row is meaningful.
    /// </summary>
    public static IReadOnlyList<WarReplayObservation> SelectDecisionPoints(
        IReadOnlyList<WarReplayObservation> observations,
        DateTimeOffset decisionCutoffUtc,
        TimeSpan minimumSpacing)
    {
        if (minimumSpacing <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumSpacing));

        var known = KnownAt(observations, decisionCutoffUtc);
        if (known.Count == 0)
            return [];

        var selected = new List<WarReplayObservation> { known[0] };
        var previousObserved = known[0];
        for (var i = 1; i < known.Count; i++)
        {
            var current = known[i];
            var lastSelected = selected[^1];
            var spacingReached = current.KnownAtUtc - lastSelected.KnownAtUtc >= minimumSpacing;
            var chainReset = current.FactionChain < previousObserved.FactionChain
                || current.OpponentChain < previousObserved.OpponentChain;
            var targetReached = current.FactionScore >= current.TargetScore
                || current.OpponentScore >= current.TargetScore;

            if (spacingReached || chainReset || targetReached)
                selected.Add(current);

            previousObserved = current;
        }

        return selected;
    }

    /// <summary>
    /// Naive comparison baseline required by #91: linearly extrapolate each side's score rate from
    /// the first known observation to the current decision point using exactly the same known-at
    /// inputs as any future model under evaluation.
    /// </summary>
    public static WarReplayBaselinePrediction PredictLinearBaseline(
        IReadOnlyList<WarReplayObservation> observations,
        DateTimeOffset decisionAtUtc)
    {
        var known = KnownAt(observations, decisionAtUtc);
        if (known.Count < 2)
            return UnknownPrediction(known, decisionAtUtc);

        var first = known[0];
        var current = known[^1];
        var elapsedSeconds = (current.SampledAtUtc - first.SampledAtUtc).TotalSeconds;
        if (elapsedSeconds <= 0)
            return UnknownPrediction(known, decisionAtUtc);

        var factionRate = Math.Max(0d, (current.FactionScore - first.FactionScore) / elapsedSeconds);
        var opponentRate = Math.Max(0d, (current.OpponentScore - first.OpponentScore) / elapsedSeconds);
        var factionFinish = ProjectFinish(current.FactionScore, current.TargetScore, factionRate, decisionAtUtc);
        var opponentFinish = ProjectFinish(current.OpponentScore, current.TargetScore, opponentRate, decisionAtUtc);
        var predictedWinner = PickWinner(factionFinish, opponentFinish);

        return new WarReplayBaselinePrediction(
            current.WarId,
            decisionAtUtc,
            predictedWinner,
            factionFinish,
            opponentFinish,
            factionRate,
            opponentRate,
            known.Count);
    }

    public static WarReplayMetrics EvaluateBaseline(
        WarReplayCase replayCase,
        TimeSpan minimumDecisionSpacing)
    {
        ValidateCase(replayCase);
        var decisionPoints = SelectDecisionPoints(
            replayCase.Observations,
            replayCase.EndedAtUtc,
            minimumDecisionSpacing);

        var predictions = decisionPoints
            .Where(point => point.KnownAtUtc < replayCase.EndedAtUtc)
            .Select(point => PredictLinearBaseline(replayCase.Observations, point.KnownAtUtc))
            .ToArray();

        var scored = predictions
            .Where(prediction => prediction.PredictedWinner != WarReplayWinner.Unknown)
            .ToArray();
        var correct = scored.Count(prediction => prediction.PredictedWinner == replayCase.ActualWinner);
        var etaErrors = scored
            .Select(prediction => prediction.PredictedWarFinishUtc)
            .Where(finish => finish.HasValue)
            .Select(finish => Math.Abs((finish!.Value - replayCase.EndedAtUtc).TotalSeconds))
            .ToArray();

        return new WarReplayMetrics(
            predictions.Length,
            scored.Length,
            predictions.Length == 0 ? 0m : (decimal)scored.Length / predictions.Length,
            scored.Length == 0 ? null : (double)correct / scored.Length,
            etaErrors.Length == 0 ? null : etaErrors.Average());
    }

    /// <summary>
    /// Splits only at war boundaries, oldest wars first. This makes row-level random leakage
    /// impossible: every observation from a war stays on the same side of the split.
    /// </summary>
    public static WarReplayChronologicalSplit SplitChronologically(
        IReadOnlyCollection<WarReplayCase> cases,
        int trainingWarCount)
    {
        ArgumentNullException.ThrowIfNull(cases);
        if (trainingWarCount < 0 || trainingWarCount > cases.Count)
            throw new ArgumentOutOfRangeException(nameof(trainingWarCount));

        foreach (var replayCase in cases)
            ValidateCase(replayCase);

        var ordered = cases.OrderBy(replayCase => replayCase.StartedAtUtc).ThenBy(replayCase => replayCase.WarId).ToArray();
        if (ordered.Select(replayCase => replayCase.WarId).Distinct().Count() != ordered.Length)
            throw new ArgumentException("Replay cases must contain unique war ids.", nameof(cases));

        return new WarReplayChronologicalSplit(
            ordered.Take(trainingWarCount).ToArray(),
            ordered.Skip(trainingWarCount).ToArray());
    }

    public static void ValidateTimeline(IReadOnlyList<WarReplayObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
            return;

        var warId = observations[0].WarId;
        for (var i = 0; i < observations.Count; i++)
        {
            var current = observations[i];
            if (current.WarId != warId)
                throw new ArgumentException("A replay timeline may contain observations from only one war.", nameof(observations));
            if (current.KnownAtUtc < current.SampledAtUtc)
                throw new ArgumentException("An observation cannot be known before it was sampled.", nameof(observations));
            RequireUtc(current.SampledAtUtc, nameof(observations));
            RequireUtc(current.KnownAtUtc, nameof(observations));

            if (i == 0)
                continue;

            var previous = observations[i - 1];
            if (current.SampledAtUtc < previous.SampledAtUtc || current.KnownAtUtc < previous.KnownAtUtc)
                throw new ArgumentException("Replay observations must remain in their original chronological order.", nameof(observations));
        }
    }

    private static void ValidateCase(WarReplayCase replayCase)
    {
        ArgumentNullException.ThrowIfNull(replayCase);
        if (replayCase.WarId <= 0) throw new ArgumentOutOfRangeException(nameof(replayCase.WarId));
        RequireUtc(replayCase.StartedAtUtc, nameof(replayCase.StartedAtUtc));
        RequireUtc(replayCase.EndedAtUtc, nameof(replayCase.EndedAtUtc));
        if (replayCase.EndedAtUtc <= replayCase.StartedAtUtc)
            throw new ArgumentException("Replay case must end after it starts.", nameof(replayCase));
        if (replayCase.ActualWinner is not (WarReplayWinner.Faction or WarReplayWinner.Opponent))
            throw new ArgumentOutOfRangeException(nameof(replayCase.ActualWinner));
        if (replayCase.Observations.Count == 0)
            throw new ArgumentException("Replay case must contain observations.", nameof(replayCase));

        ValidateTimeline(replayCase.Observations);
        if (replayCase.Observations.Any(observation => observation.WarId != replayCase.WarId))
            throw new ArgumentException("Replay observation war id must match its case.", nameof(replayCase));
        if (replayCase.Observations.Any(observation => observation.KnownAtUtc > replayCase.EndedAtUtc))
            throw new ArgumentException("Completed replay input cannot become known after the recorded war end.", nameof(replayCase));
    }

    private static WarReplayBaselinePrediction UnknownPrediction(
        IReadOnlyList<WarReplayObservation> known,
        DateTimeOffset decisionAtUtc)
    {
        var warId = known.Count == 0 ? 0 : known[^1].WarId;
        return new WarReplayBaselinePrediction(
            warId,
            decisionAtUtc,
            WarReplayWinner.Unknown,
            null,
            null,
            0d,
            0d,
            known.Count);
    }

    private static DateTimeOffset? ProjectFinish(
        int currentScore,
        int targetScore,
        double scorePerSecond,
        DateTimeOffset decisionAtUtc)
    {
        if (currentScore >= targetScore)
            return decisionAtUtc;
        if (scorePerSecond <= 0d)
            return null;

        var seconds = (targetScore - currentScore) / scorePerSecond;
        return decisionAtUtc.AddSeconds(seconds);
    }

    private static WarReplayWinner PickWinner(DateTimeOffset? factionFinish, DateTimeOffset? opponentFinish)
    {
        if (factionFinish is null && opponentFinish is null)
            return WarReplayWinner.Unknown;
        if (opponentFinish is null)
            return WarReplayWinner.Faction;
        if (factionFinish is null)
            return WarReplayWinner.Opponent;
        if (factionFinish.Value == opponentFinish.Value)
            return WarReplayWinner.Unknown;
        return factionFinish.Value < opponentFinish.Value ? WarReplayWinner.Faction : WarReplayWinner.Opponent;
    }

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Replay timestamps must be explicit UTC values.", parameterName);
    }
}
