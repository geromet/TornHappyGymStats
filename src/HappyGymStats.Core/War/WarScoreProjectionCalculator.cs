using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

internal static class WarScoreProjectionCalculator
{
    internal static WarScoreRateWindow DeriveScoreRateWindow(
        long factionId,
        IReadOnlyList<WarScoreSampleEntity> samples,
        ICollection<string> warnings)
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
        var windowSeconds = (int)Math.Round(
            (last.SampledAtUtc - first.SampledAtUtc).TotalSeconds,
            MidpointRounding.AwayFromZero);
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

    internal static WarEtaEstimate DeriveEta(int remainingScore, WarScoreRateWindow scoreRate)
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

    internal static WarAttacksToFinishEstimate DeriveAttacksToFinish(
        int remainingScore,
        int currentScore,
        IEnumerable<WarRosterSnapshotEntity> factionGroup)
    {
        var totalAttacks = factionGroup.Sum(row => Math.Max(0, row.Attacks));
        var totalScore = Math.Max(0, currentScore);
        if (remainingScore == 0)
        {
            return new WarAttacksToFinishEstimate
            {
                AverageScorePerAttack = totalAttacks == 0
                    ? null
                    : decimal.Round(totalScore / (decimal)totalAttacks, 4, MidpointRounding.AwayFromZero),
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

        var averageScorePerAttack = decimal.Round(
            totalScore / (decimal)totalAttacks,
            4,
            MidpointRounding.AwayFromZero);
        var requiredAttacks = (int)Math.Ceiling(remainingScore / averageScorePerAttack);
        return new WarAttacksToFinishEstimate
        {
            AverageScorePerAttack = averageScorePerAttack,
            RequiredAttacks = Math.Max(0, requiredAttacks),
            IsAvailable = true,
        };
    }

    internal static int ResolveFactionScore(WarScoreSampleEntity sample, long factionId)
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

    internal static int ResolveLatestFactionScore(
        WarScoreSampleEntity? sample,
        long factionId,
        int fallbackScore)
    {
        if (sample is null)
        {
            return fallbackScore;
        }

        var score = ResolveFactionScore(sample, factionId);
        return score > 0 ? score : fallbackScore;
    }

    internal static int ResolveLatestFactionChain(
        WarScoreSampleEntity? sample,
        long factionId,
        int fallbackChain)
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
