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

        var normalizedIdleIds = BuildIdleAttackerSet(rosterRows, idleAttackerIds, warnings);
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
                .Select(row => DeriveMemberState(row, asOfUtc, normalizedIdleIds.Contains(row.MemberId)))
                .ToArray();
            memberStateByFactionId[factionGroup.Key.FactionId] = members.ToList();

            var scoreRate = DeriveScoreRateWindow(factionGroup.Key.FactionId, boundedSamples, warnings);
            var rosterScore = factionGroup.Sum(row => Math.Max(0, row.Score));
            var rosterChain = factionGroup.Sum(row => Math.Max(0, row.Chain));
            var currentScore = ResolveLatestFactionScore(boundedSamples.LastOrDefault(), factionGroup.Key.FactionId, rosterScore);
            var currentChain = ResolveLatestFactionChain(boundedSamples.LastOrDefault(), factionGroup.Key.FactionId, rosterChain);
            var remainingScore = Math.Max(0, winningScore - currentScore);
            var availableMembers = members.Count(member => member.Availability == WarMemberAvailabilityKind.Available);
            var idleAvailableMembers = members.Count(member => member.IsIdleAttacker && member.Availability == WarMemberAvailabilityKind.Available);
            var coverageRatio = availableMembers == 0
                ? 1m
                : decimal.Round((availableMembers - idleAvailableMembers) / (decimal)availableMembers, 4, MidpointRounding.AwayFromZero);

            derivedFactions.Add(new WarDerivedFactionState
            {
                FactionId = factionGroup.Key.FactionId,
                FactionName = factionGroup.Key.FactionName,
                Score = currentScore,
                Chain = currentChain,
                RemainingScoreToWin = remainingScore,
                AvailableMemberCount = availableMembers,
                HospitalizedMemberCount = members.Count(member => member.Availability == WarMemberAvailabilityKind.Hospitalized),
                UnavailableMemberCount = members.Count(member => member.Availability is WarMemberAvailabilityKind.Unavailable or WarMemberAvailabilityKind.Unknown),
                CoverageRatio = coverageRatio,
                ScoreRate = scoreRate,
                Eta = DeriveEta(remainingScore, scoreRate),
                AttacksToFinish = DeriveAttacksToFinish(remainingScore, currentScore, factionGroup),
                Members = members,
            });
        }

        var holes = DeriveHoles(derivedFactions, memberStateByFactionId)
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
                    : decimal.Round(openTargets / (decimal)faction.AvailableMemberCount, 4, MidpointRounding.AwayFromZero);

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
                var chainTimer = ResolveChainTimer(faction, orderedSamples, asOfUtc);

                return withCoverage with
                {
                    ChainState = chainState,
                    ChainTimer = chainTimer,
                    ChainAlert = ChainTracker.AlertLevel(chainState, chainTimer),
                };
            })
            .ToList();

        var totalAvailable = derivedFactions.Sum(faction => faction.AvailableMemberCount);
        var totalCovered = derivedFactions.Sum(faction => (int)Math.Round(faction.CoverageRatio * faction.AvailableMemberCount, MidpointRounding.AwayFromZero));
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

    private static HashSet<long> BuildIdleAttackerSet(
        IReadOnlyCollection<WarRosterSnapshotEntity> rosterRows,
        IReadOnlyCollection<long>? explicitIdleAttackerIds,
        ICollection<string> warnings)
    {
        var rosterMemberIds = rosterRows.Select(row => row.MemberId).ToHashSet();
        var idleIds = rosterRows
            .Where(row => string.Equals(row.StatusState, "idle", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.MemberId)
            .ToHashSet();

        if (explicitIdleAttackerIds is null)
        {
            return idleIds;
        }

        foreach (var memberId in explicitIdleAttackerIds)
        {
            idleIds.Add(memberId);
            if (!rosterMemberIds.Contains(memberId))
            {
                warnings.Add($"Idle attacker id {memberId} was not present in the roster snapshot.");
            }
        }

        return idleIds;
    }

    private static WarDerivedMemberState DeriveMemberState(WarRosterSnapshotEntity row, DateTimeOffset asOfUtc, bool isIdleAttacker)
    {
        var normalizedState = row.StatusState?.Trim().ToLowerInvariant();
        var untilUtc = row.StatusUntilUtc?.ToUniversalTime();
        var hospitalCountdown = 0;
        var availability = normalizedState switch
        {
            null or "" or "okay" or "idle" => WarMemberAvailabilityKind.Available,
            "hospital" when untilUtc.HasValue && untilUtc.Value > asOfUtc => WarMemberAvailabilityKind.Hospitalized,
            "hospital" => WarMemberAvailabilityKind.Available,
            "travel" or "jail" or "federal" or "abroad" => WarMemberAvailabilityKind.Unavailable,
            _ => WarMemberAvailabilityKind.Unknown,
        };

        if (string.Equals(normalizedState, "hospital", StringComparison.Ordinal) && untilUtc.HasValue && untilUtc.Value > asOfUtc)
        {
            hospitalCountdown = (int)Math.Ceiling((untilUtc.Value - asOfUtc).TotalSeconds);
        }

        return new WarDerivedMemberState
        {
            MemberId = row.MemberId,
            MemberName = row.MemberName,
            Score = row.Score,
            Chain = row.Chain,
            Attacks = row.Attacks,
            StatusState = row.StatusState,
            StatusUntilUtc = row.StatusUntilUtc,
            Availability = availability,
            HospitalCountdownSeconds = Math.Max(0, hospitalCountdown),
            IsIdleAttacker = isIdleAttacker,
            CapturedAtUtc = row.CapturedAtUtc,
        };
    }

    private static WarScoreRateWindow DeriveScoreRateWindow(long factionId, IReadOnlyList<WarScoreSampleEntity> samples, ICollection<string> warnings)
    {
        if (samples.Count < 2)
        {
            warnings.Add($"Faction {factionId} does not have enough score samples to compute a rate.");
            return new WarScoreRateWindow
            {
                SampleCount = samples.Count,
                StartedAtUtc = samples.FirstOrDefault()?.SampledAtUtc,
                EndedAtUtc = samples.LastOrDefault()?.SampledAtUtc,
                Diagnostic = "insufficient-score-samples",
            };
        }

        var first = samples.First();
        var last = samples.Last();
        var windowSeconds = (int)Math.Round((last.SampledAtUtc - first.SampledAtUtc).TotalSeconds, MidpointRounding.AwayFromZero);
        if (windowSeconds <= 0)
        {
            warnings.Add($"Faction {factionId} score samples produced a non-positive time window.");
            return new WarScoreRateWindow
            {
                SampleCount = samples.Count,
                StartedAtUtc = first.SampledAtUtc,
                EndedAtUtc = last.SampledAtUtc,
                WindowSeconds = Math.Max(0, windowSeconds),
                Diagnostic = "invalid-score-window",
            };
        }

        var scoreDelta = ResolveFactionScore(last, factionId) - ResolveFactionScore(first, factionId);
        if (scoreDelta <= 0)
        {
            warnings.Add($"Faction {factionId} score samples produced no positive score delta.");
            return new WarScoreRateWindow
            {
                SampleCount = samples.Count,
                StartedAtUtc = first.SampledAtUtc,
                EndedAtUtc = last.SampledAtUtc,
                WindowSeconds = windowSeconds,
                ScoreDelta = scoreDelta,
                Diagnostic = "non-positive-score-delta",
            };
        }

        return new WarScoreRateWindow
        {
            SampleCount = samples.Count,
            StartedAtUtc = first.SampledAtUtc,
            EndedAtUtc = last.SampledAtUtc,
            WindowSeconds = windowSeconds,
            ScoreDelta = scoreDelta,
            PointsPerMinute = decimal.Round(scoreDelta * 60m / windowSeconds, 4, MidpointRounding.AwayFromZero),
            IsAvailable = true,
        };
    }

    private static WarEtaEstimate DeriveEta(int remainingScore, WarScoreRateWindow scoreRate)
    {
        if (remainingScore == 0)
        {
            return new WarEtaEstimate
            {
                RemainingScore = 0,
                SecondsUntilWin = 0,
                IsAvailable = true,
            };
        }

        if (!scoreRate.IsAvailable || scoreRate.PointsPerMinute is null || scoreRate.PointsPerMinute <= 0)
        {
            return new WarEtaEstimate
            {
                RemainingScore = remainingScore,
                Diagnostic = scoreRate.Diagnostic ?? "eta-unavailable",
            };
        }

        var pointsPerSecond = scoreRate.PointsPerMinute.Value / 60m;
        var secondsUntilWin = (int)Math.Ceiling(remainingScore / pointsPerSecond);
        return new WarEtaEstimate
        {
            RemainingScore = remainingScore,
            SecondsUntilWin = Math.Max(0, secondsUntilWin),
            IsAvailable = true,
        };
    }

    private static WarAttacksToFinishEstimate DeriveAttacksToFinish(int remainingScore, int currentScore, IEnumerable<WarRosterSnapshotEntity> factionGroup)
    {
        var totalAttacks = factionGroup.Sum(row => Math.Max(0, row.Attacks));
        var totalScore = Math.Max(0, currentScore);
        if (remainingScore == 0)
        {
            return new WarAttacksToFinishEstimate
            {
                AverageScorePerAttack = totalAttacks == 0 ? null : decimal.Round(totalScore / (decimal)totalAttacks, 4, MidpointRounding.AwayFromZero),
                RequiredAttacks = 0,
                IsAvailable = true,
            };
        }

        if (totalAttacks == 0 || totalScore == 0)
        {
            return new WarAttacksToFinishEstimate
            {
                Diagnostic = "no-score-per-attack-baseline",
            };
        }

        var averageScorePerAttack = decimal.Round(totalScore / (decimal)totalAttacks, 4, MidpointRounding.AwayFromZero);
        var requiredAttacks = (int)Math.Ceiling(remainingScore / averageScorePerAttack);
        return new WarAttacksToFinishEstimate
        {
            AverageScorePerAttack = averageScorePerAttack,
            RequiredAttacks = Math.Max(0, requiredAttacks),
            IsAvailable = true,
        };
    }

    private static IReadOnlyList<WarHoleRecord> DeriveHoles(
        IReadOnlyList<WarDerivedFactionState> factions,
        IReadOnlyDictionary<long, List<WarDerivedMemberState>> memberStateByFactionId)
    {
        if (factions.Count == 0)
        {
            return [];
        }

        var holes = new List<WarHoleRecord>();
        foreach (var faction in factions)
        {
            var opponent = factions.FirstOrDefault(candidate => candidate.FactionId != faction.FactionId);
            var members = memberStateByFactionId[faction.FactionId];

            foreach (var member in members.Where(member => member.IsIdleAttacker))
            {
                holes.Add(new WarHoleRecord
                {
                    Kind = WarHoleKind.IdleAttacker,
                    Severity = ResolveIdleSeverity(member),
                    FactionId = faction.FactionId,
                    FactionName = faction.FactionName,
                    OpponentFactionId = opponent?.FactionId,
                    MemberId = member.MemberId,
                    MemberName = member.MemberName,
                    Reason = member.Availability == WarMemberAvailabilityKind.Available
                        ? "Available attacker is marked idle."
                        : "Idle attacker feed references a member who is not currently available.",
                });
            }

            if (opponent is null)
            {
                continue;
            }

            // An open slot is a first-class board object per data/V2/handoff/04: an attackable
            // opponent target. "Who is free" and "who is available to hit" are the same question,
            // so this does NOT depend on this faction having idle attackers, and a target being
            // idle does not disqualify it - an idle enemy is a prime target. A hospitalised enemy
            // is a slot that regenerates at status.until, not a hole; that is already handled here
            // by requiring Availability == Available (hospital -> Hospitalized).
            // KNOWN INCOMPLETE: the handoff's "with no live claim against them" cannot be applied
            // until M010 adds ClaimTarget - every attackable target is reported until then.
            foreach (var target in opponent.Members.Where(member => member.Availability == WarMemberAvailabilityKind.Available))
            {
                holes.Add(new WarHoleRecord
                {
                    Kind = WarHoleKind.OpenTarget,
                    Severity = WarHoleSeverity.Medium,
                    FactionId = faction.FactionId,
                    FactionName = faction.FactionName,
                    OpponentFactionId = opponent.FactionId,
                    MemberId = target.MemberId,
                    MemberName = target.MemberName,
                    Reason = target.IsIdleAttacker
                        ? $"Opponent {target.MemberName} is attackable and idle."
                        : $"Opponent {target.MemberName} is attackable with no claim recorded.",
                });
            }
        }

        return holes;
    }

    private static WarHoleSeverity ResolveIdleSeverity(WarDerivedMemberState member)
        => member.Availability switch
        {
            WarMemberAvailabilityKind.Available => WarHoleSeverity.Critical,
            WarMemberAvailabilityKind.Hospitalized => WarHoleSeverity.High,
            WarMemberAvailabilityKind.Unavailable => WarHoleSeverity.High,
            _ => WarHoleSeverity.Medium,
        };

    private static int ResolveFactionScore(WarScoreSampleEntity sample, long factionId)
    {
        if (sample.FactionId == factionId)
        {
            return sample.FactionScore;
        }

        if (sample.OpponentFactionId == factionId)
        {
            return sample.OpponentScore;
        }

        return 0;
    }

    /// <summary>
    /// Torn's own deadline when the newest sample carries one, the sampled-history inference
    /// otherwise (M008 S01 sweep, 2026-09-03).
    ///
    /// The exact path is preferred because <c>end</c> is absolute — it does not decay as the
    /// sample ages, so the board can tick a live countdown rather than render a stale number
    /// with an error bar. Only the newest sample is consulted: an older deadline describes a
    /// chain that may have lapsed and restarted since, and a countdown walked off that would be
    /// confidently wrong, which is the failure the inferred path already guards against.
    ///
    /// A deadline already in the past is NOT used. Torn stops reporting a chain the moment it
    /// lapses, so a past deadline means our newest sample predates the lapse; claiming an exact
    /// "0 seconds left" from it would assert the chain is alive and expiring when it is already
    /// gone. The inference is honest about that case, so it handles it.
    /// </summary>
    private static ChainLapseEstimate ResolveChainTimer(
        WarDerivedFactionState faction,
        WarScoreSampleEntity[] orderedSamples,
        DateTimeOffset asOfUtc)
    {
        var newest = orderedSamples.LastOrDefault();
        if (newest?.FactionChainLapsesAtUtc is { } lapsesAt
            && newest.FactionId == faction.FactionId
            && lapsesAt > asOfUtc)
        {
            return ChainLapseEstimate.FromDeadline(lapsesAt, asOfUtc);
        }

        return ChainLapseInference.Infer(
            Array.ConvertAll(orderedSamples, s => (s.SampledAtUtc, Chain: ResolveFactionChain(s, faction.FactionId))),
            asOfUtc);
    }

    private static int ResolveFactionChain(WarScoreSampleEntity sample, long factionId)
    {
        if (sample.FactionId == factionId)
        {
            return sample.FactionChain;
        }

        if (sample.OpponentFactionId == factionId)
        {
            return sample.OpponentChain;
        }

        return 0;
    }

    private static int ResolveLatestFactionScore(WarScoreSampleEntity? sample, long factionId, int fallbackScore)
    {
        if (sample is null)
        {
            return fallbackScore;
        }

        var score = ResolveFactionScore(sample, factionId);
        return score > 0 ? score : fallbackScore;
    }

    private static int ResolveLatestFactionChain(WarScoreSampleEntity? sample, long factionId, int fallbackChain)
    {
        if (sample is null)
        {
            return fallbackChain;
        }

        if (sample.FactionId == factionId)
        {
            return sample.FactionChain;
        }

        if (sample.OpponentFactionId == factionId)
        {
            return sample.OpponentChain;
        }

        return fallbackChain;
    }
}
