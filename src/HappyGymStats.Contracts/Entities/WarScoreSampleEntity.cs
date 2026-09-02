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
}
