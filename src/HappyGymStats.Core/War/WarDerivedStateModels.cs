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

    /// <summary>Roster participation: fraction of available members who are actually swinging
    /// (available minus idle, over available). Not the hand-off's "coverage ratio" - see
    /// <see cref="WarDerivedFactionState.TargetCoverageRatio"/> for that.</summary>
    public decimal CoverageRatio { get; init; }

    /// <summary>Total attackable opponent targets across both factions - the board's "open slots".</summary>
    public int OpenTargetCount { get; init; }

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

    /// <summary>Roster participation for this faction: (available - idle) / available.</summary>
    public decimal CoverageRatio { get; init; }

    /// <summary>Attackable members of the opposing faction - open slots this faction can hit.</summary>
    public int OpenTargetCount { get; init; }

    /// <summary>
    /// The hand-off's coverage ratio (<c>data/V2/handoff/04</c>): attackable opponent targets over
    /// this faction's available attackers. <c>0</c> when the faction has no available attacker (it
    /// can cover nothing). <b>Proxy</b> - the denominator should be "members with energy", which
    /// needs tier-1 key data (M009); until then it is the available-member count, so a faction whose
    /// available members are all idle still reads as fully covered.
    /// </summary>
    public decimal TargetCoverageRatio { get; init; }

    public WarScoreRateWindow ScoreRate { get; init; } = new();
    public WarEtaEstimate Eta { get; init; } = new();
    public WarAttacksToFinishEstimate AttacksToFinish { get; init; } = new();

    /// <summary>Chain-command snapshot for this faction (<c>data/V2/handoff/06</c>): multiplier,
    /// next milestone, reservation window, filler-eligibility mode. <c>null</c> only before the
    /// derivation engine has run chain evaluation.</summary>
    public ChainTrackerState? ChainState { get; init; }

    /// <summary>Inferred chain-lapse timer from score-poll history. <c>null</c> when there is no
    /// live chain; <see cref="ChainLapseConfidence.None"/> inside it when the last hit is older
    /// than the score window.</summary>
    public ChainLapseEstimate? ChainTimer { get; init; }

    /// <summary>The single loudest chain signal the board should surface for this faction.</summary>
    public ChainAlertLevel ChainAlert { get; init; }

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
