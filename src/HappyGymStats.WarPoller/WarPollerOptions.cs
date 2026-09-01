namespace HappyGymStats.WarPoller;

public sealed class WarPollerOptions
{
    public const string DefaultScopeKey = "public-war";

    public string ScopeKey { get; set; } = DefaultScopeKey;
    public string ApiKey { get; set; } = string.Empty;
    public long FactionId { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;
    public int FailureBackoffSeconds { get; set; } = 60;
    public int MaxFailureBackoffSeconds { get; set; } = 300;

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
    }
}
