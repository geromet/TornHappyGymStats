namespace HappyGymStats.Api;

public sealed record ImportStatusDto(
    string Id,
    string Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PagesFetched,
    long LogsFetched,
    long LogsAppended,
    string? ErrorMessage);
