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

    // A per-war residual this close (as a fraction of the bonus) to a chain-milestone bonus is
    // treated as that lump. Wider than literal rounding on purpose: the baseline is the *faction*
    // median score/attack, so an above-median member's residual drifts up by roughly
    // attacks * (their rate - faction rate) before any lump is involved. Too tight and real lumps
    // on strong members are missed; too loose and a strong-but-lumpless member's best war is
    // discarded, which understates the opponent - the worse error for a scouting tool.
    private const decimal LumpResidualToleranceFraction = 0.12m;

    // Ignore the small early milestones (chain 10..100, bonus 10..80): a residual that size sits
    // within ordinary per-war variance against a faction-median baseline and would false-positive
    // normal above-average wars. Chains of 250+ - the ones that actually distort scouting - clear
    // this floor. Multi-milestone wars (residual near a *sum* of bonuses) are a known blind spot.
    private const int MinDetectableLumpBonus = 100;

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

        // The lump detector's baseline: the median of every (member, war) score-per-attack for this
        // faction. Zero-attack wars are excluded so a large idle roster doesn't drag it toward zero.
        var perMemberWarRates = members
            .GroupBy(m => (m.MemberId, m.WarId))
            .Select(g => (Score: g.Sum(r => r.Score), Attacks: g.Sum(r => r.Attacks)))
            .Where(x => x.Attacks > 0)
            .Select(x => (decimal)x.Score / x.Attacks)
            .OrderBy(rate => rate)
            .ToArray();
        var factionMedianScorePerAttack = Math.Round(Median(perMemberWarRates), 4);

        var profiles = members
            .GroupBy(m => m.MemberId)
            .Select(group => BuildMemberProfile(
                group.Key, group.ToArray(), totalWarsObserved, warStartById, factionMedianScorePerAttack))
            .OrderByDescending(p => p.LumpAdjustedScorePerAttack)
            .ThenByDescending(p => p.ParticipationRate)
            .ToArray();

        var idleProneCount = profiles.Count(p => p.ThreatTier == OpponentThreatTier.IdleProne);
        var factionMetrics = BuildFactionMetrics(factionId, wars, members);

        return new FactionScoutProfile(
            factionId,
            factionName,
            totalWarsObserved,
            earliestStart,
            latestStart,
            ActiveMemberCount: profiles.Length - idleProneCount,
            IdleProneMemberCount: idleProneCount,
            MedianScorePerAttack: factionMedianScorePerAttack,
            WinRate: factionMetrics.WinRate,
            WarsWithKnownOutcome: factionMetrics.WarsWithKnownOutcome,
            TypicalTargetScore: factionMetrics.TypicalTargetScore,
            PointsPerHour: factionMetrics.PointsPerHour,
            TypicalRosterSize: factionMetrics.TypicalRosterSize,
            Top5ScoreShare: factionMetrics.Top5ScoreShare,
            Top10ScoreShare: factionMetrics.Top10ScoreShare,
            profiles);
    }

    private static (
        decimal WinRate,
        int WarsWithKnownOutcome,
        int TypicalTargetScore,
        decimal? PointsPerHour,
        int TypicalRosterSize,
        decimal Top5ScoreShare,
        decimal Top10ScoreShare) BuildFactionMetrics(
        long factionId,
        IReadOnlyList<RankedWarHistoryEntity> wars,
        IReadOnlyList<RankedWarReportMemberEntity> members)
    {
        var distinctWars = wars
            .GroupBy(w => w.WarId)
            .Select(g => g.First())
            .ToArray();

        var decided = distinctWars.Where(w => w.WinnerFactionId is not null).ToArray();
        var winRate = decided.Length > 0
            ? Math.Round(decided.Count(w => w.WinnerFactionId == factionId) / (decimal)decided.Length, 4)
            : 0m;

        // "Typical target score" = this faction's own median final score - what an opponent must
        // outscore to beat them (data/V2/reference/data-layer.md, "Against a 7300 target").
        var finalScores = distinctWars
            .Select(w => ScoutedFactionScore(w, factionId))
            .Where(score => score > 0)
            .OrderBy(score => score)
            .ToArray();
        var typicalTargetScore = (int)Math.Round(Median(finalScores));

        var paces = distinctWars
            .Select(w => (Score: ScoutedFactionScore(w, factionId), Hours: (w.EndedAtUtc - w.StartedAtUtc)?.TotalHours ?? 0d))
            .Where(x => x.Score > 0 && x.Hours > 0)
            .Select(x => x.Score / (decimal)x.Hours)
            .OrderBy(pace => pace)
            .ToArray();
        decimal? pointsPerHour = paces.Length > 0 ? Math.Round(Median(paces), 2) : null;

        var rosterSizes = members
            .GroupBy(m => m.WarId)
            .Select(g => g.Select(r => r.MemberId).Distinct().Count())
            .OrderBy(size => size)
            .ToArray();
        var typicalRosterSize = (int)Math.Round(Median(rosterSizes));

        // Concentration is a per-war property - the hand-off's cited "top 5 produced 60%" is a
        // single war (48377). For each war take the top-5 / top-10 share of that war's points, then
        // the median across wars. Aggregating each member's total across all wars instead would
        // drift upward with history length as long-tenured members accumulate more points than a
        // larger rotating cast.
        var perWarShares = members
            .GroupBy(m => m.WarId)
            .Select(warGroup =>
            {
                var memberScores = warGroup
                    .GroupBy(r => r.MemberId)
                    .Select(memberGroup => memberGroup.Sum(r => r.Score))
                    .Where(score => score > 0)
                    .OrderByDescending(score => score)
                    .ToArray();
                var warTotal = memberScores.Sum();
                return warTotal == 0
                    ? ((decimal Top5, decimal Top10)?)null
                    : (memberScores.Take(5).Sum() / (decimal)warTotal, memberScores.Take(10).Sum() / (decimal)warTotal);
            })
            .Where(share => share is not null)
            .Select(share => share!.Value)
            .ToArray();

        var top5Share = perWarShares.Length > 0
            ? Math.Round(Median(perWarShares.Select(s => s.Top5).OrderBy(v => v).ToArray()), 4)
            : 0m;
        var top10Share = perWarShares.Length > 0
            ? Math.Round(Median(perWarShares.Select(s => s.Top10).OrderBy(v => v).ToArray()), 4)
            : 0m;

        return (winRate, decided.Length, typicalTargetScore, pointsPerHour, typicalRosterSize, top5Share, top10Share);
    }

    private static int ScoutedFactionScore(RankedWarHistoryEntity war, long factionId)
        => war.FactionId == factionId ? war.FactionScore ?? 0
         : war.OpponentFactionId == factionId ? war.OpponentScore ?? 0
         : 0;

    private static OpponentMemberProfile BuildMemberProfile(
        long memberId,
        IReadOnlyList<RankedWarReportMemberEntity> rows,
        int totalWarsObserved,
        IReadOnlyDictionary<long, DateTimeOffset> warStartById,
        decimal factionMedianScorePerAttack)
    {
        var latestRow = rows.OrderByDescending(r => r.CapturedAtUtc).First();
        var warsParticipated = rows.Select(r => r.WarId).Distinct().Count();
        var totalAttacks = rows.Sum(r => r.Attacks);
        var totalScore = rows.Sum(r => r.Score);
        var averageScorePerAttack = totalAttacks > 0 ? Math.Round((decimal)totalScore / totalAttacks, 2) : 0m;

        // One aggregate row per war, each tagged with the chain-milestone bonus it looks inflated by
        // (null when it looks like honest hitting).
        var perWar = rows
            .GroupBy(r => r.WarId)
            .Select(g =>
            {
                var score = g.Sum(r => r.Score);
                var attacks = g.Sum(r => r.Attacks);
                return (Score: score, Attacks: attacks, LumpBonus: DetectLumpBonus(score, attacks, factionMedianScorePerAttack));
            })
            .ToArray();

        var allWarScores = perWar.Select(w => w.Score).OrderBy(s => s).ToArray();
        var nonLumpWarScores = perWar.Where(w => w.LumpBonus is null).Select(w => w.Score).OrderBy(s => s).ToArray();
        var lumpWarCount = perWar.Count(w => w.LumpBonus is not null);

        var rawMedianScorePerWar = Math.Round(Median(allWarScores), 2);
        // Drop lump wars from the median; if every war looks lump-inflated there is nothing left to
        // compare against, so fall back to the raw median rather than reporting zero.
        var lumpAdjustedScorePerWar = nonLumpWarScores.Length > 0
            ? Math.Round(Median(nonLumpWarScores), 2)
            : rawMedianScorePerWar;

        // Median (per the hand-off spec) of each war's score/attack after subtracting that war's
        // matched milestone bonus - median rather than a weighted mean so one war's residual
        // distortion (a high-chain-multiplier stretch around the crossing hit) can't drag it.
        var adjustedPerWarRates = perWar
            .Where(w => w.Attacks > 0)
            .Select(w => (decimal)(w.Score - (w.LumpBonus ?? 0)) / w.Attacks)
            .OrderBy(rate => rate)
            .ToArray();
        var lumpAdjustedScorePerAttack = adjustedPerWarRates.Length > 0
            ? Math.Round(Median(adjustedPerWarRates), 2)
            : 0m;

        // Kept raw on purpose - the lump war genuinely happened, and its size is scouting signal.
        var maxScoreInAWar = allWarScores.Length > 0 ? allWarScores[^1] : 0;
        var minScoreInAWar = allWarScores.Length > 0 ? allWarScores[0] : 0;

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
            lumpAdjustedScorePerAttack,
            rawMedianScorePerWar,
            lumpAdjustedScorePerWar,
            lumpWarCount,
            maxScoreInAWar,
            minScoreInAWar,
            participationRate,
            idleWarCount,
            idleRate,
            lastSeenAtUtc,
            tier);
    }

    /// <summary>
    /// Returns the chain-milestone bonus a war's score looks inflated by, or <c>null</c> when the
    /// score is consistent with sustained hitting. <c>residual = score - attacks * factionMedian</c>;
    /// a positive residual within <see cref="LumpResidualToleranceFraction"/> of a
    /// <see cref="ChainEngine.MilestoneBonuses"/> value (above <see cref="MinDetectableLumpBonus"/>)
    /// is that lump.
    /// </summary>
    private static int? DetectLumpBonus(int warScore, int warAttacks, decimal factionMedianScorePerAttack)
    {
        if (warAttacks <= 0 || factionMedianScorePerAttack <= 0)
        {
            // No usable baseline (e.g. a faction with no attacking history) - can't tell a lump
            // from honest hitting, so don't guess.
            return null;
        }

        var residual = warScore - warAttacks * factionMedianScorePerAttack;
        if (residual <= 0)
        {
            return null;
        }

        int? best = null;
        var bestDistance = decimal.MaxValue;
        foreach (var bonus in ChainEngine.MilestoneBonuses)
        {
            if (bonus < MinDetectableLumpBonus)
            {
                continue;
            }

            var distance = Math.Abs(residual - bonus);
            if (distance <= bonus * LumpResidualToleranceFraction && distance < bestDistance)
            {
                best = bonus;
                bestDistance = distance;
            }
        }

        return best;
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

    private static decimal Median(IReadOnlyList<decimal> sortedValues)
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
