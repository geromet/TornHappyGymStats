namespace HappyGymStats.Core.War;

/// <summary>How the time-since-last-hit figure was arrived at.</summary>
public enum ChainLapseConfidence
{
    /// <summary>No usable estimate — fewer than two samples, or the chain was never seen rising
    /// inside the sampled window (the last qualifying hit is older than the data we hold).</summary>
    None,

    /// <summary>Estimated from the gap between score-poll samples. Resolution is the poll spacing,
    /// so treat every figure as ± one <see cref="ChainLapseEstimate.SampleSpacingSeconds"/>.</summary>
    Inferred,
}

/// <summary>
/// A coarse read on the chain-lapse timer from score-poll history. This is <b>not</b> a countdown —
/// the poller samples every ~30 s, so "seconds until lapse" carries an error bar of one sample
/// spacing. The board should render it as "last hit ~N min ago (inferred, ±Ms)", never a ticking
/// clock. A precise timer needs M008 S01's live <c>/v2/faction/chain</c> <c>timeout</c> field.
/// </summary>
public sealed record ChainLapseEstimate(
    DateTimeOffset? LastChainIncreaseAtUtc,
    int? SecondsSinceLastIncrease,
    int? SecondsUntilLapse,
    int SampleSpacingSeconds,
    bool IsInferred,
    ChainLapseConfidence Confidence,
    string Diagnostic)
{
    public static ChainLapseEstimate Unknown(string diagnostic) =>
        new(null, null, null, 0, false, ChainLapseConfidence.None, diagnostic);
}

/// <summary>
/// Pure chain-lapse inference (<c>data/V2/handoff/06-milestone-3-chain-command.md</c>, task 3 —
/// "inferred fallback is acceptable, the screen must say so").
/// </summary>
public static class ChainLapseInference
{
    /// <summary>
    /// Torn's chain-lapse timeout: a chain drops if no qualifying hit lands within this many
    /// seconds of the previous one. This is <b>not</b> the per-milestone time allowance in
    /// <see cref="ChainEngine.BonusTable"/> — that <c>Timer</c> column is the cumulative limit to
    /// <i>reach</i> the next milestone; this is the gap-between-hits limit that kills the chain.
    ///
    /// UNVERIFIED ASSUMPTION. The <c>data/V2/handoff/00-brief.md</c> assumption ledger names three
    /// unknowns (FF formula, hospital duration, energy model); this is a fourth, added here.
    /// 300 s is the Torn community constant. When M008 S01's live chain sweep lands, replace this
    /// inference with the real <c>timeout</c> field and delete the constant. The test
    /// <c>ChainLapseInference_timeout_constant_is_challengeable</c> fails loudly if the value is
    /// edited so the change cannot pass silently.
    /// </summary>
    public const int TornChainLapseTimeoutSeconds = 300;

    /// <summary>
    /// Given score-poll samples for one faction (each carrying that faction's chain length at the
    /// sample time) ordered oldest-first, estimate when the chain last advanced and how long is
    /// left before it lapses. Returns <see cref="ChainLapseConfidence.None"/> when the chain is
    /// never seen rising in the window — the board must then say "last hit time unknown", not
    /// assume a full timer.
    /// </summary>
    public static ChainLapseEstimate Infer(
        IReadOnlyList<(DateTimeOffset SampledAtUtc, int Chain)> samples,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count < 2)
        {
            return ChainLapseEstimate.Unknown("Fewer than two score samples — cannot infer a chain-lapse timer.");
        }

        var ordered = samples.OrderBy(s => s.SampledAtUtc).ToArray();
        var spacing = MedianSpacingSeconds(ordered);
        var latestChain = ordered[^1].Chain;

        DateTimeOffset? lastIncreaseAt = null;
        var chainAtLastIncrease = 0;
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].Chain > ordered[i - 1].Chain)
            {
                lastIncreaseAt = ordered[i].SampledAtUtc;
                chainAtLastIncrease = ordered[i].Chain;
            }
        }

        if (lastIncreaseAt is null)
        {
            var windowSeconds = (int)Math.Round((ordered[^1].SampledAtUtc - ordered[0].SampledAtUtc).TotalSeconds, MidpointRounding.AwayFromZero);
            return ChainLapseEstimate.Unknown(
                latestChain > 0
                    ? $"Chain {latestChain} not observed rising across {ordered.Length} samples ({windowSeconds}s window); last hit is older than the score history — lapse timer unknown."
                    : "No live chain in the score history — no lapse timer to show.");
        }

        // A Torn chain only increments or resets to 0. If the chain has dropped since the last
        // increase we saw, that increase belonged to a chain that has since lapsed — do NOT walk
        // a live countdown off a dead chain (it would eventually cross the alert threshold and
        // raise a red "about to lapse" banner for a chain that is already gone).
        if (latestChain < chainAtLastIncrease || latestChain == 0)
        {
            return ChainLapseEstimate.Unknown(
                $"Chain rose to {chainAtLastIncrease} then dropped to {latestChain} in the score history — the current chain has lapsed, no timer to show.");
        }

        var since = (int)Math.Round((nowUtc - lastIncreaseAt.Value).TotalSeconds, MidpointRounding.AwayFromZero);
        since = Math.Max(0, since);
        var untilLapse = Math.Max(0, TornChainLapseTimeoutSeconds - since);

        return new ChainLapseEstimate(
            LastChainIncreaseAtUtc: lastIncreaseAt,
            SecondsSinceLastIncrease: since,
            SecondsUntilLapse: untilLapse,
            SampleSpacingSeconds: spacing,
            IsInferred: true,
            Confidence: ChainLapseConfidence.Inferred,
            Diagnostic: $"Last qualifying hit ~{since}s ago (inferred from score polls, ±{spacing}s).");
    }

    private static int MedianSpacingSeconds((DateTimeOffset SampledAtUtc, int Chain)[] ordered)
    {
        var gaps = new List<double>(ordered.Length - 1);
        for (var i = 1; i < ordered.Length; i++)
        {
            gaps.Add((ordered[i].SampledAtUtc - ordered[i - 1].SampledAtUtc).TotalSeconds);
        }

        gaps.Sort();
        var mid = gaps.Count / 2;
        var median = gaps.Count % 2 == 1 ? gaps[mid] : (gaps[mid - 1] + gaps[mid]) / 2.0;
        return Math.Max(1, (int)Math.Round(median, MidpointRounding.AwayFromZero));
    }
}
