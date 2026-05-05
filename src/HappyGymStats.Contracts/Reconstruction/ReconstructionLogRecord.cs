namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// Storage-agnostic typed Torn log record consumed by reconstruction extraction.
/// Database adapters should map their persisted shape into this contract.
/// </summary>
public sealed record ReconstructionLogRecord(
    string LogId,
    DateTimeOffset OccurredAtUtc,
    string? Title,
    int? HappyUsed,
    int? HappyIncreased,
    int? HappyDecreased,
    int? MaxHappyBefore,
    int? MaxHappyAfter);
