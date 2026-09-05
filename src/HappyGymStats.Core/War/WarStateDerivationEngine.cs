using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

public sealed class WarStateDerivationEngine(int winningScore = WarStateDerivationEngine.DefaultWinningScore, int maxScoreSamples = WarStateDerivationEngine.DefaultMaxScoreSamples)
{
    public const int DefaultWinningScore = 1_000;
    public const int DefaultMaxScoreSamples = 8;

    public WarDerivedState Derive(
        IReadOnlyCollection<WarRosterSnapshotEntity> rosterRows,
        IReadOnlyCollection<WarScoreSampleEntity> scoreSamples,
        DateTimeOffset asOfUtc,
        IReadOnlyCollection<long>? idleAttackerIds = null)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var boundedSamples = scoreSamples
            .OrderBy(sample => sample.SampledAtUtc)
            .TakeLast(Math.Max(2, maxScoreSamples))
            .ToArray();

        if (rosterRows.Count == 0)
        {
            warnings.Add("No roster snapshot rows were provided.");
            return new WarDerivedState
            {
                WarId = boundedSamples.LastOrDefault()?.WarId,
                AsOfUtc = asOfUtc,
                ScoreWindowStartedAtUtc = boundedSamples.FirstOrDefault()?.SampledAtUtc,
                ScoreWindowEndedAtUtc = boundedSamples.LastOrDefault()?.SampledAtUtc,
                ScoreSampleCount = boundedSamples.Length,
                CoverageRatio = 1m,
                Warnings = warnings,
                Errors = errors,
            };
        }

        var normalizedIdleIds = WarMemberDerivationCalculator.BuildIdleAttackerSet(
            rosterRows,
            idleAttackerIds,
            warnings);
        var factions = rosterRows
            .GroupBy(row => new { row.FactionId, row.FactionName })
            .OrderBy(group => group.Key.FactionId)
            .ToArray();
        var memberStateByFactionId = new Dictionary<long, List<WarDerivedMemberState>>();
        var derivedFactions = new List<WarDerivedFactionState>(factions.Length);

        foreach (var factionGroup in factions)
        {
            var members = factionGroup
                .OrderBy(row => row.MemberId)
                .Select(row => WarMemberDerivationCalculator.DeriveMemberState(
                    row,
                    asOfUtc,
                    normalizedIdleIds.Contains(row.MemberId)))
                .ToArray();
            memberStateByFactionId[factionGroup.Key.FactionId] = members.ToList();

            var scoreRate = WarScoreProjectionCalculator.DeriveScoreRateWindow(
                factionGroup.Key.FactionId,
                boundedSamples,
                warnings);
            var rosterScore = factionGroup.Sum(row => Math.Max(0, row.Score));
            var rosterChain = factionGroup.Sum(row => Math.Max(0, row.Chain));
            var currentScore = WarScoreProjectionCalculator.ResolveLatestFactionScore(
                boundedSamples.LastOrDefault(),
                factionGroup.Key.FactionId,
                rosterScore);
            var currentChain = WarScoreProjectionCalculator.ResolveLatestFactionChain(
                boundedSamples.LastOrDefault(),
                factionGroup.Key.FactionId,
                rosterChain);
            var remainingScore = Math.Max(0, winningScore - currentScore);
            var coverage = WarMemberDerivationCalculator.CalculateCoverage(members);

            derivedFactions.Add(new WarDerivedFactionState
            {
                FactionId = factionGroup.Key.FactionId,
                FactionName = factionGroup.Key.FactionName,
                Score = currentScore,
                Chain = currentChain,
                RemainingScoreToWin = remainingScore,
                AvailableMemberCount = coverage.AvailableMemberCount,
                HospitalizedMemberCount = coverage.HospitalizedMemberCount,
                UnavailableMemberCount = coverage.UnavailableMemberCount,
                CoverageRatio = coverage.CoverageRatio,
                ScoreRate = scoreRate,
                Eta = WarScoreProjectionCalculator.DeriveEta(remainingScore, scoreRate),
                AttacksToFinish = WarScoreProjectionCalculator.DeriveAttacksToFinish(
                    remainingScore,
                    currentScore,
                    factionGroup),
                Members = members,
            });
        }

        var holes = WarHoleCalculator.DeriveHoles(derivedFactions, memberStateByFactionId)
            .OrderByDescending(hole => hole.Severity)
            .ThenBy(hole => hole.Kind)
            .ThenBy(hole => hole.FactionId)
            .ThenBy(hole => hole.MemberId)
            .ToArray();

        // Fold each faction's open-slot count (and the handoff's "attackable targets vs our
        // available attackers" coverage ratio) back onto the faction state now that holes exist.
        var openTargetsByFactionId = holes
            .Where(hole => hole.Kind == WarHoleKind.OpenTarget)
            .GroupBy(hole => hole.FactionId)
            .ToDictionary(group => group.Key, group => group.Count());

        // Full (un-bounded) sample history, oldest-first — the chain-lapse inference needs a wider
        // window than the score-rate one (a chain can sit un-hit for minutes and still be alive),
        // so it does NOT use boundedSamples.
        var orderedSamples = scoreSamples.OrderBy(sample => sample.SampledAtUtc).ToArray();
        // The poller stamps each score sample with its OWN faction id (WarPollerService). Chain
        // command is imperative advice for us — "wait or revive", "filler OK" — so it is derived
        // for our faction only; the enemy card must not show orders addressed to us or paint an
        // enemy chain nearing lapse as our red alert.
        var ourFactionId = orderedSamples.LastOrDefault()?.FactionId;

        derivedFactions = derivedFactions
            .Select(faction =>
            {
                var openTargets = openTargetsByFactionId.GetValueOrDefault(faction.FactionId, 0);
                // No available attacker => zero coverage. Never divide by a fudged 1: that would
                // report the highest ratio exactly when the faction can act least.
                var targetCoverage = faction.AvailableMemberCount == 0
                    ? 0m
                    : decimal.Round(
                        openTargets / (decimal)faction.AvailableMemberCount,
                        4,
                        MidpointRounding.AwayFromZero);

                var withCoverage = faction with
                {
                    OpenTargetCount = openTargets,
                    TargetCoverageRatio = targetCoverage,
                };

                if (ourFactionId is not long oursId || oursId != faction.FactionId)
                {
                    return withCoverage;
                }

                // data/V2/handoff/06: attackable war targets for us are the opponent's open slots,
                // i.e. our own OpenTargetCount.
                var chainState = ChainTracker.Evaluate(faction.Chain, openTargets);
                var chainTimer = WarChainTimerCalculator.ResolveChainTimer(
                    faction,
                    orderedSamples,
                    asOfUtc);

                return withCoverage with
                {
                    ChainState = chainState,
                    ChainTimer = chainTimer,
                    ChainAlert = ChainTracker.AlertLevel(chainState, chainTimer),
                };
            })
            .ToList();

        var totalAvailable = derivedFactions.Sum(faction => faction.AvailableMemberCount);
        var totalCovered = derivedFactions.Sum(faction =>
            (int)Math.Round(
                faction.CoverageRatio * faction.AvailableMemberCount,
                MidpointRounding.AwayFromZero));
        var overallCoverage = totalAvailable == 0
            ? 1m
            : decimal.Round(totalCovered / (decimal)totalAvailable, 4, MidpointRounding.AwayFromZero);

        return new WarDerivedState
        {
            WarId = rosterRows.First().WarId,
            AsOfUtc = asOfUtc,
            RosterCapturedAtUtc = rosterRows.Max(row => row.CapturedAtUtc),
            ScoreWindowStartedAtUtc = boundedSamples.FirstOrDefault()?.SampledAtUtc,
            ScoreWindowEndedAtUtc = boundedSamples.LastOrDefault()?.SampledAtUtc,
            ScoreSampleCount = boundedSamples.Length,
            CoverageRatio = overallCoverage,
            OpenTargetCount = derivedFactions.Sum(faction => faction.OpenTargetCount),
            Factions = derivedFactions,
            Holes = holes,
            Warnings = warnings,
            Errors = errors,
        };
    }
}
