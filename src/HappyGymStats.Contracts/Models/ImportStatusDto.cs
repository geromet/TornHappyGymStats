namespace HappyGymStats.Core.Models;

/// <summary>
/// Import job status payload (single definition for Api and Blazor).
/// </summary>
public sealed record ImportStatusDto(
    string Id,
    string Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PagesFetched,
    long LogsFetched,
    long LogsAppended,
    string? ErrorMessage);