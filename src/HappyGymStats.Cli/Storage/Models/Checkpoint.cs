namespace HappyGymStats.Storage.Models;

/// <summary>
/// Durable resume cursor for append-only log fetch.
/// Intentionally contains no API key or any secret values.
/// </summary>
public sealed record Checkpoint(
    string? NextUrl,
    string? LastLogId,
    DateTimeOffset? LastLogTimestamp,
    string? LastLogTitle,
    string? LastLogCategory,
    long TotalFetchedCount,
    long TotalAppendedCount,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    string? LastRunOutcome,
    string? LastErrorMessage,
    DateTimeOffset? LastErrorAt);
