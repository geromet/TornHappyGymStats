namespace HappyGymStats.Core.War;

public enum WarMemberAvailabilityKind
{
    Available,
    Hospitalized,
    Unavailable,
    Unknown,
}

public enum WarHoleKind
{
    IdleAttacker,
    OpenTarget,
}

public enum WarHoleSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public sealed record WarDerivedState
{
    public long? WarId { get; init; }
    public DateTimeOffset AsOfUtc { get; init; }
    public DateTimeOffset? RosterCapturedAtUtc { get; init; }
    public DateTimeOffset? ScoreWindowStartedAtUtc { get; init; }
    public DateTimeOffset? ScoreWindowEndedAtUtc { get; init; }
    public int ScoreSampleCount { get; init; }
    public string? HeartbeatPhase { get; init; }
    public DateTimeOffset? HeartbeatUpdatedAtUtc { get; init; }
    public DateTimeOffset? HeartbeatPollStartedAtUtc { get; init; }
    public DateTimeOffset? HeartbeatPollCompletedAtUtc { get; init; }
    public DateTimeOffset? HeartbeatStaleAfterUtc { get; init; }
    public bool IsHeartbeatStale { get; init; }
    public string? HeartbeatLastError { get; init; }
    public decimal CoverageRatio { get; init; }
    public IReadOnlyList<WarDerivedFactionState> Factions { get; init; } = [];
    public IReadOnlyList<WarHoleRecord> Holes { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record WarDerivedFactionState
{
    public long FactionId { get; init; }
    public string FactionName { get; init; } = string.Empty;
    public int Score { get; init; }
    public int Chain { get; init; }
    public int RemainingScoreToWin { get; init; }
    public int AvailableMemberCount { get; init; }
    public int HospitalizedMemberCount { get; init; }
    public int UnavailableMemberCount { get; init; }
    public decimal CoverageRatio { get; init; }
    public WarScoreRateWindow ScoreRate { get; init; } = new();
    public WarEtaEstimate Eta { get; init; } = new();
    public WarAttacksToFinishEstimate AttacksToFinish { get; init; } = new();
    public IReadOnlyList<WarDerivedMemberState> Members { get; init; } = [];
}

public sealed record WarDerivedMemberState
{
    public long MemberId { get; init; }
    public string MemberName { get; init; } = string.Empty;
    public int Score { get; init; }
    public int Chain { get; init; }
    public int Attacks { get; init; }
    public string? StatusState { get; init; }
    public DateTimeOffset? StatusUntilUtc { get; init; }
    public WarMemberAvailabilityKind Availability { get; init; }
    public int HospitalCountdownSeconds { get; init; }
    public bool IsIdleAttacker { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
}

public sealed record WarScoreRateWindow
{
    public int SampleCount { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public int WindowSeconds { get; init; }
    public int ScoreDelta { get; init; }
    public decimal? PointsPerMinute { get; init; }
    public bool IsAvailable { get; init; }
    public string? Diagnostic { get; init; }
}

public sealed record WarEtaEstimate
{
    public int RemainingScore { get; init; }
    public int? SecondsUntilWin { get; init; }
    public bool IsAvailable { get; init; }
    public string? Diagnostic { get; init; }
}

public sealed record WarAttacksToFinishEstimate
{
    public decimal? AverageScorePerAttack { get; init; }
    public int? RequiredAttacks { get; init; }
    public bool IsAvailable { get; init; }
    public string? Diagnostic { get; init; }
}

public sealed record WarHoleRecord
{
    public WarHoleKind Kind { get; init; }
    public WarHoleSeverity Severity { get; init; }
    public long FactionId { get; init; }
    public string FactionName { get; init; } = string.Empty;
    public long? OpponentFactionId { get; init; }
    public long MemberId { get; init; }
    public string MemberName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
