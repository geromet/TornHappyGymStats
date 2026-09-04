namespace HappyGymStats.Core.Models;

/// <summary>
/// The three values <c>WarStateDto.Status</c> can take. They are not
/// interchangeable and the distinction is load-bearing: <see cref="NotReady"/>
/// means there is no war on, which is an ordinary state of the world, while
/// <see cref="Degraded"/> means the board cannot be trusted. Rendering the
/// first as if it were the second told operators the API was down on every
/// quiet evening.
/// </summary>
public static class WarStatus
{
    /// <summary>A war is running and the board is trustworthy.</summary>
    public const string Ok = "ok";

    /// <summary>No war is currently running. Not an error.</summary>
    public const string NotReady = "not-ready";

    /// <summary>A war is running, but the state has errors or a stale heartbeat.</summary>
    public const string Degraded = "degraded";
}

/// <summary>
/// Shared war board DTOs (single definition for Api and Blazor). Mapping from Core
/// derived-state records lives in <c>HappyGymStats.Api.Models.WarDtoMapper</c>.
/// </summary>
public sealed record WarStateDto(
    string Status,
    bool IsReady,
    long? WarId,
    DateTimeOffset AsOfUtc,
    bool HasRoster,
    int FactionCount,
    int MemberCount,
    decimal CoverageRatio,
    int OpenTargetCount,
    int HoleCount,
    WarHeartbeatDto Heartbeat,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<WarFactionDto> Factions,
    IReadOnlyList<WarHoleDto> Holes);

public sealed record WarHealthDto(
    string Status,
    bool IsReady,
    long? WarId,
    DateTimeOffset AsOfUtc,
    bool HasRoster,
    int FactionCount,
    int MemberCount,
    decimal CoverageRatio,
    int OpenTargetCount,
    int HoleCount,
    WarHeartbeatDto Heartbeat,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WarHeartbeatDto(
    string? Phase,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PollStartedAtUtc,
    DateTimeOffset? PollCompletedAtUtc,
    DateTimeOffset? StaleAfterUtc,
    bool IsStale,
    string? LastError);

public sealed record WarFactionDto(
    long FactionId,
    string FactionName,
    int Score,
    int Chain,
    int RemainingScoreToWin,
    int AvailableMemberCount,
    int HospitalizedMemberCount,
    int UnavailableMemberCount,
    decimal CoverageRatio,
    int OpenTargetCount,
    decimal TargetCoverageRatio,
    WarChainCommandDto? ChainCommand,
    WarScoreRateDto ScoreRate,
    WarEtaDto Eta,
    WarAttacksToFinishDto AttacksToFinish,
    IReadOnlyList<WarMemberDto> Members);

/// <summary>
/// Chain-command panel data for one faction (<c>data/V2/handoff/06</c>). Flattened from
/// <see cref="HappyGymStats.Core.War.ChainTrackerState"/> and the inferred lapse timer — the
/// board renders it directly and derives nothing.
/// </summary>
public sealed record WarChainCommandDto(
    int ChainLength,
    double CurrentMultiplier,
    int? NextMilestone,
    int? HitsToNextMilestone,
    int NextMilestoneBonus,
    bool IsInReservationWindow,
    int ForfeitedValueIfCrossedOutside,
    int AttackableWarTargetCount,
    string Mode,
    string Advice,
    string Alert,
    bool TimerIsInferred,
    int? SecondsSinceLastHit,
    int? SecondsUntilLapse,
    int TimerSpacingSeconds,
    string? TimerDiagnostic,
    // "None" | "Inferred" | "Exact". TimerIsInferred is kept rather than replaced: it is
    // already consumed, and the two answer different questions — whether the figure carries an
    // error bar, and where it came from.
    string TimerConfidence,
    // Absolute lapse instant, present only when TimerConfidence is "Exact". The board ticks
    // against this so the countdown stays true between polls; SecondsUntilLapse is a snapshot
    // taken when the state was derived and goes stale the moment it is sent.
    DateTimeOffset? LapsesAtUtc);

public sealed record WarMemberDto(
    long MemberId,
    string MemberName,
    int Score,
    int Chain,
    int Attacks,
    string? StatusState,
    DateTimeOffset? StatusUntilUtc,
    string Availability,
    int HospitalCountdownSeconds,
    bool IsIdleAttacker,
    DateTimeOffset CapturedAtUtc);

public sealed record WarScoreRateDto(
    int SampleCount,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int WindowSeconds,
    int ScoreDelta,
    decimal? PointsPerMinute,
    bool IsAvailable,
    string? Diagnostic);

public sealed record WarEtaDto(
    int RemainingScore,
    int? SecondsUntilWin,
    bool IsAvailable,
    string? Diagnostic);

public sealed record WarAttacksToFinishDto(
    decimal? AverageScorePerAttack,
    int? RequiredAttacks,
    bool IsAvailable,
    string? Diagnostic);

public sealed record WarHoleDto(
    string Kind,
    string Severity,
    long FactionId,
    string FactionName,
    long? OpponentFactionId,
    long MemberId,
    string MemberName,
    string Reason);

public sealed record WarNotifyAcceptedDto(string Status, WarStateDto State);
