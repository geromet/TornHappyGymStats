namespace HappyGymStats.Data.Entities;

public sealed class WarCurrentEntity
{
    public string ScopeKey { get; set; } = string.Empty;
    public long? WarId { get; set; }
    public long? FactionId { get; set; }
    public string? FactionName { get; set; }
    public long? OpponentFactionId { get; set; }
    public string? OpponentFactionName { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }
    public bool IsLive { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
}
