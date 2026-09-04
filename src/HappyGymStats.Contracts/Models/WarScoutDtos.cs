namespace HappyGymStats.Core.Models;

/// <summary>
/// Shared scouting DTOs (single definition for Api and Blazor). Mapping from Core
/// profiles lives in <c>HappyGymStats.Api.Models.WarScoutDtoMapper</c>.
/// </summary>
public sealed record FactionScoutDto(
    long FactionId,
    string FactionName,
    int TotalWarsObserved,
    DateTimeOffset? EarliestWarStartedAtUtc,
    DateTimeOffset? LatestWarStartedAtUtc,
    int ActiveMemberCount,
    int IdleProneMemberCount,
    decimal MedianScorePerAttack,
    decimal WinRate,
    int WarsWithKnownOutcome,
    int TypicalTargetScore,
    decimal? PointsPerHour,
    int TypicalRosterSize,
    decimal Top5ScoreShare,
    decimal Top10ScoreShare,
    IReadOnlyList<OpponentMemberProfileDto> Members);

public sealed record OpponentMemberProfileDto(
    long MemberId,
    string MemberName,
    int WarsParticipated,
    int TotalAttacks,
    int TotalScore,
    decimal AverageScorePerAttack,
    decimal LumpAdjustedScorePerAttack,
    decimal RawMedianScorePerWar,
    decimal LumpAdjustedScorePerWar,
    int LumpWarCount,
    int MaxScoreInAWar,
    int MinScoreInAWar,
    decimal ParticipationRate,
    int IdleWarCount,
    decimal IdleRate,
    DateTimeOffset? LastSeenAtUtc,
    string ThreatTier);
