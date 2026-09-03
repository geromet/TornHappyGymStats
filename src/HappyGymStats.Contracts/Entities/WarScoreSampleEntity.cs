namespace HappyGymStats.Data.Entities;

public sealed class WarScoreSampleEntity
{
    public long Id { get; set; }
    public long WarId { get; set; }
    public long FactionId { get; set; }
    public string FactionName { get; set; } = string.Empty;
    public int FactionScore { get; set; }
    public int FactionChain { get; set; }
    public long OpponentFactionId { get; set; }
    public string OpponentFactionName { get; set; } = string.Empty;
    public int OpponentScore { get; set; }
    public int OpponentChain { get; set; }
    public DateTimeOffset SampledAtUtc { get; set; }

    /// <summary>
    /// When OUR faction's chain lapses, from Torn's own <c>end</c> field
    /// (<c>/v2/faction?selections=chain</c>, M008 S01 sweep). Absolute, so unlike the
    /// <c>timeout</c> countdown it stays true as the sample ages.
    ///
    /// Null when no chain is running, or when the chain call failed — the derivation then falls
    /// back to <see cref="HappyGymStats.Core.War.ChainLapseInference"/>. There is deliberately no
    /// opponent equivalent: the selection only reports the chain of the faction the key belongs
    /// to, so the enemy card can never have an exact timer.
    /// </summary>
    public DateTimeOffset? FactionChainLapsesAtUtc { get; set; }
}
