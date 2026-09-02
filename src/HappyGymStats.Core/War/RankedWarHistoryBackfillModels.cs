namespace HappyGymStats.Core.War;

public static class RankedWarHistoryBackfillStatus
{
    public const string NotStarted = "NotStarted";
    public const string Running = "Running";
    public const string WaitingRetry = "WaitingRetry";
    public const string Completed = "Completed";
}

public static class RankedWarHistoryBackfillPhase
{
    public const string Idle = "Idle";
    public const string FetchingHistoryPage = "FetchingHistoryPage";
    public const string FetchingReport = "FetchingReport";
    public const string Disabled = "Disabled";
}

/// <summary>
/// Classifies why a ranked-war history backfill iteration failed, so retryable Torn/network
/// conditions can be distinguished from terminal configuration/payload problems that need
/// operator attention rather than another automatic retry.
/// </summary>
public enum RankedWarHistoryBackfillFailureCategory
{
    RateLimited,
    TransientHttp,
    MalformedResponse,
    InvalidConfiguration,
    IngestValidation,
    Unexpected,
}

public sealed record RankedWarHistoryBackfillIterationResult(
    string Status,
    string Phase,
    TimeSpan DelayBeforeNextIteration,
    RankedWarHistoryBackfillFailureCategory? FailureCategory);
