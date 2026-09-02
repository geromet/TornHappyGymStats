namespace HappyGymStats.Data.Entities;

public sealed class WarPollerHeartbeatEntity
{
    public string ScopeKey { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? PollStartedAtUtc { get; set; }
    public DateTimeOffset? PollCompletedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public long? ActiveWarId { get; set; }
    public DateTimeOffset? StaleAfterUtc { get; set; }
    public int PollIntervalSeconds { get; set; }
    public int FailureBackoffSeconds { get; set; }
}
