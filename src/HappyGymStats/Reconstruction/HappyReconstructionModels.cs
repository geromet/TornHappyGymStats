namespace HappyGymStats.Reconstruction;

/// <summary>
/// Shared record types used by happy reconstruction.
/// </summary>
/// <remarks>
/// These are intentionally small and stable: downstream tasks will add fields as the
/// reconstruction pipeline (read → extract → timeline → reconstruct → write) is implemented.
/// </remarks>
public static class HappyReconstructionModels
{
    /// <summary>
    /// A typed event extracted from a raw user log record.
    /// </summary>
    public abstract record ReconstructionEvent(
        string LogId,
        DateTimeOffset OccurredAtUtc);

    /// <summary>
    /// A parsed gym train event extracted from a user log.
    /// </summary>
    public sealed record GymTrainEvent(
        string LogId,
        DateTimeOffset OccurredAtUtc,
        int HappyUsed)
        : ReconstructionEvent(LogId, OccurredAtUtc);

    /// <summary>
    /// A parsed "max happy" change extracted from a user log.
    /// </summary>
    public sealed record MaxHappyEvent(
        string LogId,
        DateTimeOffset OccurredAtUtc,
        int MaxHappy)
        : ReconstructionEvent(LogId, OccurredAtUtc);

    /// <summary>
    /// Derived reconstruction output for a single gym train log.
    /// </summary>
    public sealed record DerivedGymTrain(
        string LogId,
        DateTimeOffset OccurredAtUtc,
        int HappyBeforeTrain,
        int HappyUsed,
        int HappyAfterTrain,
        long RegenTicksApplied,
        int RegenHappyGained,
        int? MaxHappyAtTimeUtc,
        bool ClampedToMax);

    /// <summary>
    /// High-level reconstruction stats suitable for a UI summary panel.
    /// </summary>
    public sealed record ReconstructionStats(
        int LinesRead,
        int MalformedLines,
        int GymTrainEventsExtracted,
        int MaxHappyEventsExtracted,
        int GymTrainsDerived,
        int ClampAppliedCount,
        int WarningCount);
}
