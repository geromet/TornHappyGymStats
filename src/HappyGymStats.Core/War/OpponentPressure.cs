namespace HappyGymStats.Core.War;

public enum OpponentPressureLevel
{
    Unknown = 0,
    Normal = 1,
    Elevated = 2,
    Surge = 3,
}

/// <summary>
/// Provider-neutral inputs assembled from already-sampled faction status observations and,
/// when already available, the existing authoritative attack-event stream. This type owns no
/// transport or polling behavior.
/// </summary>
public sealed record OpponentPressureInput
{
    public required DateTimeOffset AsOfUtc { get; init; }
    public required DateTimeOffset WindowStartUtc { get; init; }
    public required DateTimeOffset FreshestObservationAtUtc { get; init; }
    public required int FactionMemberCount { get; init; }
    public required int ObservedMemberCount { get; init; }
    public required int ActiveMemberCount { get; init; }
    public required int AttackableMemberCount { get; init; }
    public int SynchronizedAttackableTransitions { get; init; }
    public int RecentAttackCount { get; init; }
    public decimal? BaselineActiveShare { get; init; }
    public decimal? BaselineAttackableShare { get; init; }
    public required int BaselineSampleCount { get; init; }
    public IReadOnlyList<string> Provenance { get; init; } = [];
    public OpponentPressurePriorState? PriorState { get; init; }
}

public sealed record OpponentPressurePriorState
{
    public required OpponentPressureLevel Level { get; init; }
    public required DateTimeOffset SinceUtc { get; init; }
}

/// <summary>
/// Auditable derived pressure signal. The level describes observable activity relative to the
/// opponent's own baseline; it is not a prediction of intent.
/// </summary>
public sealed record OpponentPressureSignal
{
    public required OpponentPressureLevel Level { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required DateTimeOffset WindowStartUtc { get; init; }
    public required DateTimeOffset FreshestObservationAtUtc { get; init; }
    public required int FactionMemberCount { get; init; }
    public required int ObservedMemberCount { get; init; }
    public required decimal Coverage { get; init; }
    public required int ActiveMemberCount { get; init; }
    public required int AttackableMemberCount { get; init; }
    public required decimal ActiveShare { get; init; }
    public required decimal AttackableShare { get; init; }
    public decimal? BaselineActiveShare { get; init; }
    public decimal? BaselineAttackableShare { get; init; }
    public required int BaselineSampleCount { get; init; }
    public required int SynchronizedAttackableTransitions { get; init; }
    public required int RecentAttackCount { get; init; }
    public required bool HeldByHysteresis { get; init; }
    public required string Explanation { get; init; }
    public IReadOnlyList<string> Provenance { get; init; } = [];
}

public static class OpponentPressureEngine
{
    public const int MinimumObservedMembers = 5;
    public const int MinimumBaselineSamples = 12;
    public const decimal MinimumCoverage = 0.50m;
    public static readonly TimeSpan MaximumObservationAge = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ElevatedDowngradeCooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SurgeDowngradeCooldown = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(2);

    public static OpponentPressureSignal Evaluate(OpponentPressureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        var asOf = input.AsOfUtc.ToUniversalTime();
        var freshest = input.FreshestObservationAtUtc.ToUniversalTime();
        var coverage = input.FactionMemberCount == 0
            ? 0m
            : (decimal)input.ObservedMemberCount / input.FactionMemberCount;
        var activeShare = input.ObservedMemberCount == 0
            ? 0m
            : (decimal)input.ActiveMemberCount / input.ObservedMemberCount;
        var attackableShare = input.ObservedMemberCount == 0
            ? 0m
            : (decimal)input.AttackableMemberCount / input.ObservedMemberCount;

        var insufficientReason = GetInsufficientEvidenceReason(input, asOf, freshest, coverage);
        if (insufficientReason is not null)
        {
            return BuildSignal(
                input,
                OpponentPressureLevel.Unknown,
                coverage,
                activeShare,
                attackableShare,
                heldByHysteresis: false,
                insufficientReason);
        }

        var rawLevel = Classify(input, activeShare, attackableShare);
        var (level, held) = ApplyHysteresis(rawLevel, input.PriorState, asOf);
        var explanation = Explain(input, rawLevel, level, activeShare, attackableShare, held);

        return BuildSignal(input, level, coverage, activeShare, attackableShare, held, explanation);
    }

    private static string? GetInsufficientEvidenceReason(
        OpponentPressureInput input,
        DateTimeOffset asOf,
        DateTimeOffset freshest,
        decimal coverage)
    {
        if (input.ObservedMemberCount < MinimumObservedMembers)
        {
            return $"Unknown: only {input.ObservedMemberCount} members observed; at least {MinimumObservedMembers} are required.";
        }

        if (coverage < MinimumCoverage)
        {
            return $"Unknown: observation coverage {coverage:P0} is below the {MinimumCoverage:P0} minimum.";
        }

        if (input.BaselineSampleCount < MinimumBaselineSamples)
        {
            return $"Unknown: baseline has {input.BaselineSampleCount} samples; at least {MinimumBaselineSamples} are required.";
        }

        if (input.BaselineActiveShare is null && input.BaselineAttackableShare is null)
        {
            return "Unknown: no historical active or attackable baseline is available.";
        }

        var age = asOf - freshest;
        if (age > MaximumObservationAge)
        {
            return $"Unknown: freshest status observation is {age.TotalMinutes:0.#} minutes old.";
        }

        return null;
    }

    private static OpponentPressureLevel Classify(
        OpponentPressureInput input,
        decimal activeShare,
        decimal attackableShare)
    {
        var activeExcess = ExcessMembers(input.ActiveMemberCount, input.ObservedMemberCount, input.BaselineActiveShare);
        var attackableExcess = ExcessMembers(input.AttackableMemberCount, input.ObservedMemberCount, input.BaselineAttackableShare);

        var activeSurge = MeetsRelativeThreshold(activeShare, input.BaselineActiveShare, multiplier: 2.0m, absoluteLift: 0.20m);
        var attackableSurge = MeetsRelativeThreshold(attackableShare, input.BaselineAttackableShare, multiplier: 2.0m, absoluteLift: 0.20m);
        var coordinatedEvidence = input.SynchronizedAttackableTransitions >= 3 || input.RecentAttackCount >= 3;
        if ((activeSurge && activeExcess >= 4 || attackableSurge && attackableExcess >= 4) && coordinatedEvidence)
        {
            return OpponentPressureLevel.Surge;
        }

        var activeElevated = MeetsRelativeThreshold(activeShare, input.BaselineActiveShare, multiplier: 1.5m, absoluteLift: 0.10m);
        var attackableElevated = MeetsRelativeThreshold(attackableShare, input.BaselineAttackableShare, multiplier: 1.5m, absoluteLift: 0.10m);
        if (activeElevated && activeExcess >= 2 || attackableElevated && attackableExcess >= 2 ||
            input.SynchronizedAttackableTransitions >= 2 && (activeElevated || attackableElevated))
        {
            return OpponentPressureLevel.Elevated;
        }

        return OpponentPressureLevel.Normal;
    }

    private static (OpponentPressureLevel Level, bool Held) ApplyHysteresis(
        OpponentPressureLevel rawLevel,
        OpponentPressurePriorState? priorState,
        DateTimeOffset asOf)
    {
        if (priorState is null || priorState.Level is OpponentPressureLevel.Unknown or OpponentPressureLevel.Normal)
        {
            return (rawLevel, false);
        }

        if (rawLevel >= priorState.Level || rawLevel == OpponentPressureLevel.Unknown)
        {
            return (rawLevel, false);
        }

        var cooldown = priorState.Level == OpponentPressureLevel.Surge
            ? SurgeDowngradeCooldown
            : ElevatedDowngradeCooldown;
        var elapsed = asOf - priorState.SinceUtc.ToUniversalTime();
        return elapsed < cooldown ? (priorState.Level, true) : (rawLevel, false);
    }

    private static bool MeetsRelativeThreshold(
        decimal observedShare,
        decimal? baselineShare,
        decimal multiplier,
        decimal absoluteLift)
    {
        if (baselineShare is null)
        {
            return false;
        }

        var threshold = Math.Max(baselineShare.Value * multiplier, baselineShare.Value + absoluteLift);
        return observedShare >= Math.Min(1m, threshold);
    }

    private static decimal ExcessMembers(int observedCount, int sampleSize, decimal? baselineShare)
    {
        if (baselineShare is null)
        {
            return 0m;
        }

        return observedCount - baselineShare.Value * sampleSize;
    }

    private static string Explain(
        OpponentPressureInput input,
        OpponentPressureLevel rawLevel,
        OpponentPressureLevel level,
        decimal activeShare,
        decimal attackableShare,
        bool held)
    {
        var baselineActive = input.BaselineActiveShare?.ToString("P0") ?? "n/a";
        var baselineAttackable = input.BaselineAttackableShare?.ToString("P0") ?? "n/a";
        var core = $"Observed {input.ActiveMemberCount}/{input.ObservedMemberCount} active ({activeShare:P0}, baseline {baselineActive}) and " +
                   $"{input.AttackableMemberCount}/{input.ObservedMemberCount} attackable ({attackableShare:P0}, baseline {baselineAttackable}); " +
                   $"{input.SynchronizedAttackableTransitions} synchronized transitions and {input.RecentAttackCount} recent attacks.";
        return held
            ? $"{core} Raw level {rawLevel} is held at {level} by downgrade hysteresis."
            : $"{core} Derived level: {level}.";
    }

    private static OpponentPressureSignal BuildSignal(
        OpponentPressureInput input,
        OpponentPressureLevel level,
        decimal coverage,
        decimal activeShare,
        decimal attackableShare,
        bool heldByHysteresis,
        string explanation)
    {
        return new OpponentPressureSignal
        {
            Level = level,
            EvaluatedAtUtc = input.AsOfUtc.ToUniversalTime(),
            WindowStartUtc = input.WindowStartUtc.ToUniversalTime(),
            FreshestObservationAtUtc = input.FreshestObservationAtUtc.ToUniversalTime(),
            FactionMemberCount = input.FactionMemberCount,
            ObservedMemberCount = input.ObservedMemberCount,
            Coverage = coverage,
            ActiveMemberCount = input.ActiveMemberCount,
            AttackableMemberCount = input.AttackableMemberCount,
            ActiveShare = activeShare,
            AttackableShare = attackableShare,
            BaselineActiveShare = input.BaselineActiveShare,
            BaselineAttackableShare = input.BaselineAttackableShare,
            BaselineSampleCount = input.BaselineSampleCount,
            SynchronizedAttackableTransitions = input.SynchronizedAttackableTransitions,
            RecentAttackCount = input.RecentAttackCount,
            HeldByHysteresis = heldByHysteresis,
            Explanation = explanation,
            Provenance = input.Provenance.ToArray(),
        };
    }

    private static void Validate(OpponentPressureInput input)
    {
        if (input.FactionMemberCount < 0 || input.ObservedMemberCount < 0 || input.ObservedMemberCount > input.FactionMemberCount)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Observed members must be between zero and the faction member count.");
        }

        if (input.ActiveMemberCount < 0 || input.ActiveMemberCount > input.ObservedMemberCount ||
            input.AttackableMemberCount < 0 || input.AttackableMemberCount > input.ObservedMemberCount)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Active and attackable counts must be within the observed member count.");
        }

        if (input.SynchronizedAttackableTransitions < 0 || input.SynchronizedAttackableTransitions > input.ObservedMemberCount || input.RecentAttackCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Transition and attack counts cannot be negative or exceed their applicable sample size.");
        }

        ValidateShare(input.BaselineActiveShare, nameof(input.BaselineActiveShare));
        ValidateShare(input.BaselineAttackableShare, nameof(input.BaselineAttackableShare));

        if (input.BaselineSampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input.BaselineSampleCount));
        }

        var asOf = input.AsOfUtc.ToUniversalTime();
        var windowStart = input.WindowStartUtc.ToUniversalTime();
        var freshest = input.FreshestObservationAtUtc.ToUniversalTime();
        if (windowStart > asOf)
        {
            throw new ArgumentException("Pressure window cannot start after evaluation time.", nameof(input.WindowStartUtc));
        }

        if (freshest < windowStart)
        {
            throw new ArgumentException("Freshest observation must fall within the declared pressure window.", nameof(input.FreshestObservationAtUtc));
        }

        if (freshest > asOf + MaximumFutureSkew)
        {
            throw new ArgumentException("Observation timestamp is implausibly ahead of the evaluation clock.", nameof(input.FreshestObservationAtUtc));
        }

        if (input.PriorState is not null)
        {
            if (!Enum.IsDefined(input.PriorState.Level))
            {
                throw new ArgumentOutOfRangeException(nameof(input.PriorState), input.PriorState.Level, "Prior pressure level must be a defined value.");
            }

            if (input.PriorState.SinceUtc.ToUniversalTime() > asOf)
            {
                throw new ArgumentException("Prior pressure state cannot begin in the future.", nameof(input.PriorState));
            }
        }
    }

    private static void ValidateShare(decimal? value, string parameterName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Baseline shares must be between zero and one.");
        }
    }
}
