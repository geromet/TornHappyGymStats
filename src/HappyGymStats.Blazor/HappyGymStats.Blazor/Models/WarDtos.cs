namespace HappyGymStats.Blazor.Models;

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
    string? TimerDiagnostic);

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
