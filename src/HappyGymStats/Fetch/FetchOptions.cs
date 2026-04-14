namespace HappyGymStats.Fetch;

public sealed record FetchOptions(
    Uri FreshStartUrl,
    TimeSpan ThrottleDelay,
    int MaxRetryAttempts,
    TimeSpan InitialBackoffDelay,
    TimeSpan MaxBackoffDelay)
{
    public static FetchOptions Default(Uri freshStartUrl, TimeSpan throttleDelay)
        => new(
            FreshStartUrl: freshStartUrl,
            ThrottleDelay: throttleDelay,
            MaxRetryAttempts: 5,
            InitialBackoffDelay: TimeSpan.FromSeconds(2),
            MaxBackoffDelay: TimeSpan.FromSeconds(30));
}
