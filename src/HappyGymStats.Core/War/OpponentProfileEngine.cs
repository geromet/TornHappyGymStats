using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

/// <summary>
/// Pure aggregation of stored ranked-war history/report rows into per-member opponent scouting
/// profiles. Takes no dependency on Torn or persistence so it can be unit tested deterministically.
/// </summary>
public static class OpponentProfileEngine
{
    private const decimal IdleProneThreshold = 0.5m;
    private const decimal ConsistentSwingerParticipationThreshold = 0.6m;

    public static FactionScoutProfile BuildProfile(
        long factionId,
        string factionName,
        IReadOnlyList<RankedWarHistoryEntity> wars,
        IReadOnlyList<RankedWarReportMemberEntity> members)
    {
        if (factionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "Faction id must be positive.");
        }

        ArgumentNullException.ThrowIfNull(wars);
        ArgumentNullException.ThrowIfNull(members);

        var totalWarsObserved = wars.Select(w => w.WarId).Distinct().Count();
        var earliestStart = wars.Count > 0 ? wars.Min(w => w.StartedAtUtc) : (DateTimeOffset?)null;
        var latestStart = wars.Count > 0 ? wars.Max(w => w.StartedAtUtc) : (DateTimeOffset?)null;
        var warStartById = wars
            .GroupBy(w => w.WarId)
            .ToDictionary(g => g.Key, g => g.First().StartedAtUtc);

        var profiles = members
            .GroupBy(m => m.MemberId)
            .Select(group => BuildMemberProfile(group.Key, group.ToArray(), totalWarsObserved, warStartById))
            .OrderByDescending(p => p.LumpAdjustedScorePerWar)
            .ThenByDescending(p => p.ParticipationRate)
            .ToArray();

        var idleProneCount = profiles.Count(p => p.ThreatTier == OpponentThreatTier.IdleProne);

        return new FactionScoutProfile(
            factionId,
            factionName,
            totalWarsObserved,
            earliestStart,
            latestStart,
            ActiveMemberCount: profiles.Length - idleProneCount,
            IdleProneMemberCount: idleProneCount,
            profiles);
    }

    private static OpponentMemberProfile BuildMemberProfile(
        long memberId,
        IReadOnlyList<RankedWarReportMemberEntity> rows,
        int totalWarsObserved,
        IReadOnlyDictionary<long, DateTimeOffset> warStartById)
    {
        var latestRow = rows.OrderByDescending(r => r.CapturedAtUtc).First();
        var warsParticipated = rows.Select(r => r.WarId).Distinct().Count();
        var totalAttacks = rows.Sum(r => r.Attacks);
        var totalScore = rows.Sum(r => r.Score);
        var averageScorePerAttack = totalAttacks > 0 ? Math.Round((decimal)totalScore / totalAttacks, 2) : 0m;

        var perWarScores = rows
            .GroupBy(r => r.WarId)
            .Select(g => g.Sum(r => r.Score))
            .OrderBy(score => score)
            .ToArray();
        var lumpAdjustedScorePerWar = Math.Round(Median(perWarScores), 2);
        var maxScoreInAWar = perWarScores.Length > 0 ? perWarScores[^1] : 0;
        var minScoreInAWar = perWarScores.Length > 0 ? perWarScores[0] : 0;

        var participationRate = totalWarsObserved > 0
            ? Math.Round((decimal)warsParticipated / totalWarsObserved, 4)
            : 0m;

        var idleWarCount = rows.Count(r => r.IsIdleAttacker);
        var idleRate = rows.Count > 0 ? Math.Round((decimal)idleWarCount / rows.Count, 4) : 0m;

        var lastSeenAtUtc = rows
            .Select(r => warStartById.TryGetValue(r.WarId, out var start) ? start : r.CapturedAtUtc)
            .Max();

        var tier = ClassifyTier(idleRate, participationRate);

        return new OpponentMemberProfile(
            memberId,
            latestRow.MemberName,
            warsParticipated,
            totalAttacks,
            totalScore,
            averageScorePerAttack,
            lumpAdjustedScorePerWar,
            maxScoreInAWar,
            minScoreInAWar,
            participationRate,
            idleWarCount,
            idleRate,
            lastSeenAtUtc,
            tier);
    }

    private static string ClassifyTier(decimal idleRate, decimal participationRate)
    {
        if (idleRate >= IdleProneThreshold)
        {
            return OpponentThreatTier.IdleProne;
        }

        return participationRate >= ConsistentSwingerParticipationThreshold
            ? OpponentThreatTier.ConsistentSwinger
            : OpponentThreatTier.OccasionalSwinger;
    }

    private static decimal Median(IReadOnlyList<int> sortedValues)
    {
        if (sortedValues.Count == 0)
        {
            return 0m;
        }

        var mid = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 1
            ? sortedValues[mid]
            : (sortedValues[mid - 1] + sortedValues[mid]) / 2m;
    }
}
