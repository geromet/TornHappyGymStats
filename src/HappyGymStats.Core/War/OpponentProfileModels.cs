namespace HappyGymStats.Core.War;

public static class OpponentThreatTier
{
    public const string ConsistentSwinger = "ConsistentSwinger";
    public const string OccasionalSwinger = "OccasionalSwinger";
    public const string IdleProne = "IdleProne";
}

/// <summary>
/// Faction-level scouting summary aggregated from stored ranked-war history and report rows
/// (<c>data/V2/handoff/05</c>, "Faction-level profile"). Every rate degrades to <c>0</c> / <c>null</c>
/// when the underlying history rows don't carry the inputs (winner, final scores, end time), which a
/// sparsely-backfilled war can lack.
/// </summary>
/// <param name="WinRate">Wars won over <see cref="WarsWithKnownOutcome"/>.</param>
/// <param name="WarsWithKnownOutcome">Wars whose history row has a recorded winner.</param>
/// <param name="TypicalTargetScore">This faction's own median final score - what an opponent must
/// outscore to beat them.</param>
/// <param name="PointsPerHour">Median of this faction's own final score divided by war duration;
/// <c>null</c> when no war has both a positive score and a positive duration.</param>
/// <param name="TypicalRosterSize">Median distinct members fielded per war.</param>
/// <param name="Top5ScoreShare">Median across wars of the share of a war's points produced by that
/// war's five highest-scoring members - concentration; a high value makes lockdown viable (DEATH
/// WATCH's top 5 were ~60% in war 48377).</param>
/// <param name="Top10ScoreShare">As <see cref="Top5ScoreShare"/> for the top ten.</param>
public sealed record FactionScoutProfile(
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
