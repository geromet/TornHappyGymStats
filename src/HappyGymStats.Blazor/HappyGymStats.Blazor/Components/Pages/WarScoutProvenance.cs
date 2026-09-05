using HappyGymStats.Blazor.Components.Shared;

namespace HappyGymStats.Blazor.Components.Pages;

public enum WarScoutMetric
{
    TotalWarsObserved,
    ObservedDateRange,
    SampleSufficiency,
    BackfillStatus,
    BackfillProgress,
    BackfillUpdated,
    ActiveMemberCount,
    IdleProneMemberCount,
    TypicalRosterSize,
    MembersSeen,
    WinRate,
    OutcomeSample,
    TypicalTargetScore,
    PointsPerHour,
    Top5ScoreShare,
    Top10ScoreShare,
    MedianScorePerAttack,
    LumpWarCount,
    ThreatTier,
    WarsParticipated,
    ParticipationRate,
    LumpAdjustedScorePerAttack,
    AverageScorePerAttack,
    LumpAdjustedScorePerWar,
    RawMedianScorePerWar,
    ScoreRange,
    IdleRate,
    LastSeen
}

public static class WarScoutProvenance
{
    public static FigureKind For(WarScoutMetric metric) => metric switch
    {
        WarScoutMetric.TotalWarsObserved => FigureKind.Measured,
        WarScoutMetric.ObservedDateRange => FigureKind.Measured,
        WarScoutMetric.BackfillStatus => FigureKind.Measured,
        WarScoutMetric.BackfillProgress => FigureKind.Measured,
        WarScoutMetric.BackfillUpdated => FigureKind.Measured,
        WarScoutMetric.MembersSeen => FigureKind.Measured,
        WarScoutMetric.OutcomeSample => FigureKind.Measured,
        WarScoutMetric.WarsParticipated => FigureKind.Measured,
        WarScoutMetric.ScoreRange => FigureKind.Measured,
        WarScoutMetric.LastSeen => FigureKind.Measured,

        WarScoutMetric.SampleSufficiency => FigureKind.Inferred,
        WarScoutMetric.ActiveMemberCount => FigureKind.Inferred,
        WarScoutMetric.IdleProneMemberCount => FigureKind.Inferred,
        WarScoutMetric.TypicalRosterSize => FigureKind.Inferred,
        WarScoutMetric.WinRate => FigureKind.Inferred,
        WarScoutMetric.TypicalTargetScore => FigureKind.Inferred,
        WarScoutMetric.PointsPerHour => FigureKind.Inferred,
        WarScoutMetric.Top5ScoreShare => FigureKind.Inferred,
        WarScoutMetric.Top10ScoreShare => FigureKind.Inferred,
        WarScoutMetric.MedianScorePerAttack => FigureKind.Inferred,
        WarScoutMetric.LumpWarCount => FigureKind.Inferred,
        WarScoutMetric.ThreatTier => FigureKind.Inferred,
        WarScoutMetric.ParticipationRate => FigureKind.Inferred,
        WarScoutMetric.LumpAdjustedScorePerAttack => FigureKind.Inferred,
        WarScoutMetric.AverageScorePerAttack => FigureKind.Inferred,
        WarScoutMetric.LumpAdjustedScorePerWar => FigureKind.Inferred,
        WarScoutMetric.RawMedianScorePerWar => FigureKind.Inferred,
        WarScoutMetric.IdleRate => FigureKind.Inferred,

        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown Scout metric provenance.")
    };
}
