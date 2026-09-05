using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

internal static class WarChainTimerCalculator
{
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
    internal static ChainLapseEstimate ResolveChainTimer(
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
            Array.ConvertAll(orderedSamples, sample =>
                (sample.SampledAtUtc, Chain: ResolveFactionChain(sample, faction.FactionId))),
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
}
