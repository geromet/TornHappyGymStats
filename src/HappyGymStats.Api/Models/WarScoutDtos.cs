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
    IReadOnlyList<OpponentMemberProfileDto> Members);

public sealed record OpponentMemberProfileDto(
    long MemberId,
    string MemberName,
    int WarsParticipated,
    int TotalAttacks,
    int TotalScore,
    decimal AverageScorePerAttack,
    decimal LumpAdjustedScorePerWar,
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
            Members: profile.Members.Select(ToMemberDto).ToArray());

    private static OpponentMemberProfileDto ToMemberDto(OpponentMemberProfile member)
        => new(
            MemberId: member.MemberId,
            MemberName: member.MemberName,
            WarsParticipated: member.WarsParticipated,
            TotalAttacks: member.TotalAttacks,
            TotalScore: member.TotalScore,
            AverageScorePerAttack: member.AverageScorePerAttack,
            LumpAdjustedScorePerWar: member.LumpAdjustedScorePerWar,
            MaxScoreInAWar: member.MaxScoreInAWar,
            MinScoreInAWar: member.MinScoreInAWar,
            ParticipationRate: member.ParticipationRate,
            IdleWarCount: member.IdleWarCount,
            IdleRate: member.IdleRate,
            LastSeenAtUtc: member.LastSeenAtUtc,
            ThreatTier: member.ThreatTier);
}
