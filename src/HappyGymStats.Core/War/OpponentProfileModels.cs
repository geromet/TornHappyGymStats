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
    IReadOnlyList<OpponentMemberProfile> Members);

/// <summary>
/// A per-member scouting profile aggregated from real ranked-war report outcomes. Uses the median
/// per-war score (<see cref="LumpAdjustedScorePerWar"/>) rather than a mean or sum so a single war
/// where the member happened to land a large chain-milestone bonus doesn't distort their typical
/// output the way a raw total or average would.
/// </summary>
public sealed record OpponentMemberProfile(
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
