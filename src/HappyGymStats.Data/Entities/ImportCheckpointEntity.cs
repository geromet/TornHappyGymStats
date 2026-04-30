namespace HappyGymStats.Data.Entities;

public sealed class ImportCheckpointEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = "default";

    public string? NextUrl { get; set; }

    public string? LastLogId { get; set; }

    public DateTimeOffset? LastLogTimestamp { get; set; }

    public string? LastLogTitle { get; set; }

    public string? LastLogCategory { get; set; }

    public long TotalFetchedCount { get; set; }

    public long TotalAppendedCount { get; set; }

    public DateTimeOffset? LastRunStartedAt { get; set; }

    public DateTimeOffset? LastRunCompletedAt { get; set; }

    public string? LastRunOutcome { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTimeOffset? LastErrorAt { get; set; }
}
