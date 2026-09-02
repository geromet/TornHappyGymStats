using HappyGymStats.Core.Models;
using HappyGymStats.Core.War;

namespace HappyGymStats.Api.Models;

/// <summary>
/// Maps Core scouting profiles to the shared scout DTOs
/// (<see cref="HappyGymStats.Core.Models"/>). The DTO record definitions are owned by
/// the Contracts assembly; only the mapping lives here.
/// </summary>
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