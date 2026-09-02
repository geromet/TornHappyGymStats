using HappyGymStats.Core.War;

namespace HappyGymStats.Api.Models;

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

public static class WarScoutDtoMapper
{
    public static FactionScoutDto ToDto(this FactionScoutProfile profile)
        => new(
            FactionId: profile.FactionId,
            FactionName: profile.FactionName,
            TotalWarsObserved: profile.TotalWarsObserved,
            EarliestWarStartedAtUtc: profile.EarliestWarStartedAtUtc,
            LatestWarStartedAtUtc: profile.LatestWarStartedAtUtc,
            ActiveMemberCount: profile.ActiveMemberCount,
            IdleProneMemberCount: profile.IdleProneMemberCount,
            MedianScorePerAttack: profile.MedianScorePerAttack,
            WinRate: profile.WinRate,
            WarsWithKnownOutcome: profile.WarsWithKnownOutcome,
            TypicalTargetScore: profile.TypicalTargetScore,
            PointsPerHour: profile.PointsPerHour,
            TypicalRosterSize: profile.TypicalRosterSize,
            Top5ScoreShare: profile.Top5ScoreShare,
            Top10ScoreShare: profile.Top10ScoreShare,
            Members: profile.Members.Select(ToMemberDto).ToArray());

    private static OpponentMemberProfileDto ToMemberDto(OpponentMemberProfile member)
        => new(
            MemberId: member.MemberId,
            MemberName: member.MemberName,
            WarsParticipated: member.WarsParticipated,
            TotalAttacks: member.TotalAttacks,
            TotalScore: member.TotalScore,
            AverageScorePerAttack: member.AverageScorePerAttack,
            LumpAdjustedScorePerAttack: member.LumpAdjustedScorePerAttack,
            RawMedianScorePerWar: member.RawMedianScorePerWar,
            LumpAdjustedScorePerWar: member.LumpAdjustedScorePerWar,
            LumpWarCount: member.LumpWarCount,
            MaxScoreInAWar: member.MaxScoreInAWar,
            MinScoreInAWar: member.MinScoreInAWar,
            ParticipationRate: member.ParticipationRate,
            IdleWarCount: member.IdleWarCount,
            IdleRate: member.IdleRate,
            LastSeenAtUtc: member.LastSeenAtUtc,
            ThreatTier: member.ThreatTier);
}
