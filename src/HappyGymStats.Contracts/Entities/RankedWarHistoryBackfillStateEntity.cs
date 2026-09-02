namespace HappyGymStats.Data.Entities;

public sealed class RankedWarHistoryBackfillStateEntity
{
    public string ScopeKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Phase { get; set; }
    public string? NextHistoryPageUrl { get; set; }
    public long? LastProcessedWarId { get; set; }
    public long PagesProcessed { get; set; }
    public long ReportsProcessed { get; set; }
    public int RetryCount { get; set; }
    public string? LastFailureCategory { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? LastSuccessAtUtc { get; set; }
    public DateTimeOffset? LastFailureAtUtc { get; set; }
    public DateTimeOffset? NextRetryAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
