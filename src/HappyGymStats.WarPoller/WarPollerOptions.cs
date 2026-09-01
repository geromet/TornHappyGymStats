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
    }
}
