namespace HappyGymStats.WarPoller;

public sealed class WarPollerOptions
{
    public const string SectionName = "WarPoller";
    public const string DefaultScopeKey = "public-war";

    public string ScopeKey { get; set; } = DefaultScopeKey;
    public string ApiKey { get; set; } = string.Empty;
    public long FactionId { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;
    public int FailureBackoffSeconds { get; set; } = 60;
    public int MaxFailureBackoffSeconds { get; set; } = 300;
    public int StaleThresholdSeconds { get; set; } = 120;
    public string? HubNotifyUrl { get; set; }
    public int HubNotifyTimeoutSeconds { get; set; } = 5;

    public bool RankedWarHistoryBackfillEnabled { get; set; }
    public string RankedWarHistoryBackfillScopeKey { get; set; } = "ranked-war-history-backfill";
    public int RankedWarHistoryBackfillMaxPagesPerIteration { get; set; } = 1;
    public int RankedWarHistoryBackfillMaxReportsPerIteration { get; set; } = 10;
    public int RankedWarHistoryBackfillIterationDelaySeconds { get; set; } = 5;
    public int RankedWarHistoryBackfillFailureBackoffSeconds { get; set; } = 60;
    public int RankedWarHistoryBackfillMaxFailureBackoffSeconds { get; set; } = 900;
    public string? RankedWarHistoryBackfillStartUrl { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ScopeKey))
        {
            throw new InvalidOperationException("War poller scope key must be configured.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("War poller API key must be configured.");
        }

        if (FactionId <= 0)
        {
            throw new InvalidOperationException("War poller faction id must be positive.");
        }

        if (PollIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("War poll interval must be greater than zero seconds.");
        }

        if (FailureBackoffSeconds <= 0)
        {
            throw new InvalidOperationException("War poller failure backoff must be greater than zero seconds.");
        }

        if (MaxFailureBackoffSeconds < FailureBackoffSeconds)
        {
            throw new InvalidOperationException("War poller max failure backoff must be greater than or equal to the base failure backoff.");
        }

        if (StaleThresholdSeconds < PollIntervalSeconds)
        {
            throw new InvalidOperationException("War poller stale threshold must be greater than or equal to the poll interval.");
        }

        if (!string.IsNullOrWhiteSpace(HubNotifyUrl))
        {
            if (!Uri.TryCreate(HubNotifyUrl, UriKind.Absolute, out var notifyUri)
                || (notifyUri.Scheme != Uri.UriSchemeHttp && notifyUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("War poller hub notify URL must be an absolute http(s) URL when configured.");
            }

            if (!notifyUri.IsLoopback && !string.Equals(notifyUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("War poller hub notify URL must target a loopback host.");
            }
        }

        if (HubNotifyTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("War poller hub notify timeout must be greater than zero seconds.");
        }

        if (RankedWarHistoryBackfillEnabled)
        {
            if (string.IsNullOrWhiteSpace(RankedWarHistoryBackfillScopeKey))
            {
                throw new InvalidOperationException("Ranked-war history backfill scope key must be configured when the backfill service is enabled.");
            }

            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new InvalidOperationException("Ranked-war history backfill requires a war poller API key.");
            }

            if (RankedWarHistoryBackfillMaxPagesPerIteration <= 0)
            {
                throw new InvalidOperationException("Ranked-war history backfill max pages per iteration must be greater than zero.");
            }

            if (RankedWarHistoryBackfillMaxReportsPerIteration <= 0)
            {
                throw new InvalidOperationException("Ranked-war history backfill max reports per iteration must be greater than zero.");
            }

            if (RankedWarHistoryBackfillIterationDelaySeconds <= 0)
            {
                throw new InvalidOperationException("Ranked-war history backfill iteration delay must be greater than zero seconds.");
            }

            if (RankedWarHistoryBackfillFailureBackoffSeconds <= 0)
            {
                throw new InvalidOperationException("Ranked-war history backfill failure backoff must be greater than zero seconds.");
            }

            if (RankedWarHistoryBackfillMaxFailureBackoffSeconds < RankedWarHistoryBackfillFailureBackoffSeconds)
            {
                throw new InvalidOperationException("Ranked-war history backfill max failure backoff must be greater than or equal to the base failure backoff.");
            }

            if (!string.IsNullOrWhiteSpace(RankedWarHistoryBackfillStartUrl)
                && !Uri.TryCreate(RankedWarHistoryBackfillStartUrl, UriKind.RelativeOrAbsolute, out _))
            {
                throw new InvalidOperationException("Ranked-war history backfill start URL must be a valid URL when configured.");
            }
        }
    }
}
