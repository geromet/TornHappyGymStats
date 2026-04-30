namespace HappyGymStats.Reconstruction;

/// <summary>
/// Storage-agnostic raw Torn log record consumed by reconstruction extraction.
/// File, database, and API adapters should map their persisted shape into this contract.
/// </summary>
public sealed record ReconstructionLogRecord(
    string LogId,
    DateTimeOffset OccurredAtUtc,
    string? Title,
    string? Category,
    string RawJson);
