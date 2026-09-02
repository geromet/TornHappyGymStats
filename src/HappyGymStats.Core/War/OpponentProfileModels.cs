namespace HappyGymStats.Core.War;

public static class OpponentThreatTier
{
    public const string ConsistentSwinger = "ConsistentSwinger";
    public const string OccasionalSwinger = "OccasionalSwinger";
    public const string IdleProne = "IdleProne";
}

public sealed record FactionScoutProfile(
    long FactionId,
    string FactionName,
    int TotalWarsObserved,
    DateTimeOffset? EarliestWarStartedAtUtc,
    DateTimeOffset? LatestWarStartedAtUtc,
    int ActiveMemberCount,
    int IdleProneMemberCount,
    decimal MedianScorePerAttack,
    IReadOnlyList<OpponentMemberProfile> Members);

/// <summary>
/// A per-member scouting profile aggregated from real ranked-war report outcomes.
/// <para>
/// Milestone bonuses count toward war score and are credited to whoever lands the crossing hit,
/// so one war where a member caught a lump can make them look several times better than they are
/// (the DerDoruk / war-48377 case). For each of a member's wars we compute
/// <c>residual = score - attacks * faction median score/attack</c> and, when that residual matches
/// a value in <see cref="ChainEngine.MilestoneBonuses"/>, treat the war as lump-inflated:
/// <see cref="LumpWarCount"/> counts it, <see cref="LumpAdjustedScorePerWar"/> drops it from the
/// per-war median, and <see cref="LumpAdjustedScorePerAttack"/> is the median of per-war
/// score/attack with each matched bonus removed from its war first. The raw
/// figures (<see cref="AverageScorePerAttack"/>, <see cref="RawMedianScorePerWar"/>,
/// <see cref="MinScoreInAWar"/>, <see cref="MaxScoreInAWar"/>) are kept alongside because "who
/// lands crossing hits" is itself worth knowing.
/// </para>
/// </summary>
public sealed record OpponentMemberProfile(
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
