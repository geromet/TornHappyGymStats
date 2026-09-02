using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HappyGymStats.Core.Torn;

/// <summary>
/// Priority of a Torn API call, used to shed the lowest-value work first when a key's request
/// budget runs low. Lower ordinal = higher priority (kept longest). Order and rationale are from
/// <c>data/V2/handoff/03-torn-client.md</c>.
/// </summary>
public enum TornRequestPriority
{
    /// <summary>Both faction rosters - the board is useless without them.</summary>
    Roster = 0,

    /// <summary><c>wars</c> / <c>warfareranked</c> live state - score, chain, lambda*.</summary>
    WarState = 1,

    /// <summary><c>attacksfull</c> - per-member attribution.</summary>
    AttacksFull = 2,

    /// <summary>Everything else - history backfill, on-demand scouting reports, gym-stats logs.</summary>
    Other = 3,
}

/// <summary>The outcome of a rate-limit check.</summary>
/// <param name="Acquired">True when a token was taken and the call may proceed.</param>
/// <param name="RetryAfter">When <see cref="Acquired"/> is false, how long until a retry could succeed.</param>
public readonly record struct RateLimitLease(bool Acquired, TimeSpan RetryAfter)
{
    public static RateLimitLease Ok { get; } = new(true, TimeSpan.Zero);

    public static RateLimitLease Wait(TimeSpan retryAfter) => new(false, retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);
}

/// <summary>
/// A per-key token-bucket rate limiter for the Torn API. Torn allows 100 requests/minute per key;
/// this holds a conservative ceiling (default 80/min) so on-demand calls made on a user's behalf
/// keep headroom. Every Torn call path goes through one shared instance.
/// <para>
/// Buckets are keyed by a non-reversible hash of the API key (<see cref="KeyIdentity"/>), never the
/// key itself, so the secret never becomes a dictionary key or reaches a log. On a Torn <c>code 5</c>
/// or HTTP 429 the caller reports it via <see cref="ReportThrottled"/>, which drains the bucket and
/// opens an exponential-backoff window with jitter; <see cref="ReportSuccess"/> clears the backoff
/// escalation once a clean call gets through.
/// </para>
/// </summary>
public sealed class TornRateLimiter
{
    /// <summary>Conservative default, below Torn's real 100/min/key limit.</summary>
    public const int DefaultPerMinuteCeiling = 80;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);
    private const int MaxThrottleEscalation = 8;

    private readonly int _ceiling;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public TornRateLimiter(TimeProvider? timeProvider = null, int perMinuteCeiling = DefaultPerMinuteCeiling)
    {
        if (perMinuteCeiling < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(perMinuteCeiling), perMinuteCeiling, "Ceiling must be at least 1 request/minute.");
        }

        _ceiling = perMinuteCeiling;
        _refillPerSecond = perMinuteCeiling / Window.TotalSeconds;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Requests per minute this limiter admits per key once warmed up.</summary>
    public int PerMinuteCeiling => _ceiling;

    /// <summary>
    /// Non-blocking. Takes a token for <paramref name="keyIdentity"/> if the budget allows a call of
    /// this <paramref name="priority"/>, otherwise returns how long to wait. Thread-safe.
    /// </summary>
    public RateLimitLease TryAcquire(string keyIdentity, TornRequestPriority priority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyIdentity);

        var bucket = GetBucket(keyIdentity);
        lock (bucket.Gate)
        {
            var now = _time.GetUtcNow();
            bucket.Refill(now, _refillPerSecond, _ceiling);

            // code 5 / 429 backoff window - all priorities wait it out.
            if (now < bucket.BlockedUntil)
            {
                return RateLimitLease.Wait(bucket.BlockedUntil - now);
            }

            // Priority shedding: a call may only take a token if it leaves the reserve that
            // higher-priority work is entitled to. As the bucket drains, low priorities fail first.
            var required = 1.0 + ReserveFor(priority);
            if (bucket.Tokens >= required)
            {
                bucket.Tokens -= 1.0;
                return RateLimitLease.Ok;
            }

            var deficit = required - bucket.Tokens;
            return RateLimitLease.Wait(TimeSpan.FromSeconds(deficit / _refillPerSecond));
        }
    }

    /// <summary>
    /// Waits (via the injected <see cref="TimeProvider"/>) until a token for this key and priority is
    /// available, then returns. Honours <paramref name="ct"/>. Re-checks at least every 5 seconds so a
    /// backoff opened by another thread is picked up promptly.
    /// <para>
    /// Only makes progress while the injected <see cref="TimeProvider"/>'s clock advances in real
    /// time (i.e. <see cref="TimeProvider.System"/>). A frozen fake clock means "wait forever" by
    /// definition - fake-clock tests should drive <see cref="TryAcquire"/> and assert its
    /// <see cref="RateLimitLease.RetryAfter"/> rather than awaiting this.
    /// </para>
    /// </summary>
    public async Task AcquireAsync(string keyIdentity, TornRequestPriority priority, CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var lease = TryAcquire(keyIdentity, priority);
            if (lease.Acquired)
            {
                return;
            }

            var delay = lease.RetryAfter > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : lease.RetryAfter;
            if (delay <= TimeSpan.Zero)
            {
                delay = TimeSpan.FromMilliseconds(1);
            }

            await Task.Delay(delay, _time, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Report that Torn rejected a call for this key with <c>code 5</c> or HTTP 429. Drains the bucket
    /// and opens (or extends) an exponential-backoff window with jitter.
    /// <para>
    /// The escalation level persists (each consecutive call doubles the window, up to the 2-minute
    /// cap) until <see cref="ReportSuccess"/> is called outside a backoff window. Every call path that
    /// reports throttles MUST also report successes, or a single later 429 jumps straight to the cap.
    /// <see cref="TornApiClient"/> does both.
    /// </para>
    /// </summary>
    public void ReportThrottled(string keyIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyIdentity);

        var bucket = GetBucket(keyIdentity);
        lock (bucket.Gate)
        {
            var now = _time.GetUtcNow();
            bucket.ConsecutiveThrottles = Math.Min(bucket.ConsecutiveThrottles + 1, MaxThrottleEscalation);
            bucket.Tokens = 0.0;

            var exponential = BaseBackoff * Math.Pow(2, bucket.ConsecutiveThrottles - 1);
            if (exponential > MaxBackoff)
            {
                exponential = MaxBackoff;
            }

            var backoff = exponential + exponential * 0.5 * bucket.NextJitter();
            var until = now + backoff;
            if (until > bucket.BlockedUntil)
            {
                bucket.BlockedUntil = until;
            }
        }
    }

    /// <summary>
    /// Report that a call for this key succeeded. Once outside any backoff window this resets the
    /// escalation so the next throttle starts from the base backoff again.
    /// </summary>
    public void ReportSuccess(string keyIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyIdentity);

        if (!_buckets.TryGetValue(keyIdentity, out var bucket))
        {
            return;
        }

        lock (bucket.Gate)
        {
            if (_time.GetUtcNow() >= bucket.BlockedUntil)
            {
                bucket.ConsecutiveThrottles = 0;
            }
        }
    }

    /// <summary>
    /// Non-reversible per-key dimension for the bucket store: the first 64 bits of the SHA-256 of the
    /// key, hex-encoded. Distinct keys collide with negligible probability; the key cannot be
    /// recovered from it.
    /// </summary>
    public static string KeyIdentity(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private Bucket GetBucket(string keyIdentity)
        => _buckets.GetOrAdd(keyIdentity, _ => new Bucket(_ceiling, _time.GetUtcNow()));

    private double ReserveFor(TornRequestPriority priority) => priority switch
    {
        TornRequestPriority.Roster => 0.0,
        TornRequestPriority.WarState => _ceiling * 0.10,
        TornRequestPriority.AttacksFull => _ceiling * 0.25,
        _ => _ceiling * 0.40,
    };

    private sealed class Bucket
    {
        public readonly object Gate = new();
        public double Tokens;
        public DateTimeOffset LastRefill;
        public DateTimeOffset BlockedUntil;
        public int ConsecutiveThrottles;
        private uint _rng;

        public Bucket(int ceiling, DateTimeOffset now)
        {
            Tokens = ceiling;
            LastRefill = now;
            BlockedUntil = now;
            _rng = ((uint)(now.Ticks & 0xFFFFFFFF)) | 1u;
        }

        public void Refill(DateTimeOffset now, double perSecond, int ceiling)
        {
            var elapsedSeconds = (now - LastRefill).TotalSeconds;
            if (elapsedSeconds <= 0)
            {
                return;
            }

            Tokens = Math.Min(ceiling, Tokens + elapsedSeconds * perSecond);
            LastRefill = now;
        }

        /// <summary>Deterministic xorshift jitter in [0, 1); seeded from creation time so tests with a
        /// fixed clock are reproducible.</summary>
        public double NextJitter()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / (double)0x1000000;
        }
    }
}
