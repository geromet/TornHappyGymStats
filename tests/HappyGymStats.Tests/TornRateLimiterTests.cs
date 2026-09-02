using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HappyGymStats.Core.Torn;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class TornRateLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private const string KeyA = "aaaaaaaaaaaaaaaa";
    private const string KeyB = "bbbbbbbbbbbbbbbb";

    [Fact]
    public void Ctor_rejects_a_non_positive_ceiling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TornRateLimiter(perMinuteCeiling: 0));
    }

    [Fact]
    public void A_fresh_bucket_admits_up_to_the_ceiling_then_throttles()
    {
        var time = new MutableTimeProvider(T0);
        var sut = new TornRateLimiter(time, perMinuteCeiling: 80);

        for (var i = 0; i < 80; i++)
        {
            Assert.True(sut.TryAcquire(KeyA, TornRequestPriority.Roster).Acquired, $"call {i} should be admitted");
        }

        var denied = sut.TryAcquire(KeyA, TornRequestPriority.Roster);

        Assert.False(denied.Acquired);
        Assert.True(denied.RetryAfter > TimeSpan.Zero);
        // One token refills in 60/80 = 0.75s.
        Assert.True(denied.RetryAfter <= TimeSpan.FromSeconds(1), $"retry-after was {denied.RetryAfter}");
    }

    [Fact]
    public void The_budget_refills_continuously_and_is_capped_at_the_ceiling()
    {
        var time = new MutableTimeProvider(T0);
        var sut = new TornRateLimiter(time, perMinuteCeiling: 80);
        Drain(sut, KeyA, TornRequestPriority.Roster);

        // 30s at 80/min returns ~40 tokens.
        time.Advance(TimeSpan.FromSeconds(30));
        var got = CountAdmitted(sut, KeyA, TornRequestPriority.Roster);
        Assert.InRange(got, 39, 41);

        // Well past a full window - refilled but capped at 80, not accumulated.
        time.Advance(TimeSpan.FromMinutes(5));
        var gotAfterLongIdle = CountAdmitted(sut, KeyA, TornRequestPriority.Roster);
        Assert.InRange(gotAfterLongIdle, 79, 80);
    }

    [Fact]
    public void Low_priority_work_sheds_first_as_the_bucket_drains()
    {
        var time = new MutableTimeProvider(T0);
        var sut = new TornRateLimiter(time, perMinuteCeiling: 100);

        // Drain to exactly 30 tokens (reserves: Roster 0, WarState 10, AttacksFull 25, Other 40).
        for (var i = 0; i < 70; i++)
        {
            Assert.True(sut.TryAcquire(KeyA, TornRequestPriority.Roster).Acquired);
        }

        // Other needs 30 tokens of headroom above the one it takes -> shed.
        Assert.False(sut.TryAcquire(KeyA, TornRequestPriority.Other).Acquired);
        // AttacksFull still has room.
        Assert.True(sut.TryAcquire(KeyA, TornRequestPriority.AttacksFull).Acquired);

        Drain(sut, KeyA, TornRequestPriority.AttacksFull);
        // Below AttacksFull's reserve now, but WarState and Roster still clear.
        Assert.False(sut.TryAcquire(KeyA, TornRequestPriority.AttacksFull).Acquired);
        Assert.True(sut.TryAcquire(KeyA, TornRequestPriority.WarState).Acquired);

        Drain(sut, KeyA, TornRequestPriority.WarState);
        Assert.False(sut.TryAcquire(KeyA, TornRequestPriority.WarState).Acquired);
        // Rosters are never shed while any token remains.
        Assert.True(sut.TryAcquire(KeyA, TornRequestPriority.Roster).Acquired);
    }

    [Fact]
    public void Buckets_are_isolated_per_key()
    {
        var time = new MutableTimeProvider(T0);
        var sut = new TornRateLimiter(time, perMinuteCeiling: 80);

        Drain(sut, KeyA, TornRequestPriority.Roster);

        Assert.False(sut.TryAcquire(KeyA, TornRequestPriority.Roster).Acquired);
        Assert.True(sut.TryAcquire(KeyB, TornRequestPriority.Roster).Acquired);
    }

    [Fact]
    public void ReportThrottled_opens_a_backoff_window_that_blocks_every_priority()
    {
        var time = new MutableTimeProvider(T0);
        var sut = new TornRateLimiter(time, perMinuteCeiling: 80);
        Assert.True(sut.TryAcquire(KeyA, TornRequestPriority.Roster).Acquired);

        sut.ReportThrottled(KeyA);

        var lease = sut.TryAcquire(KeyA, TornRequestPriority.Roster);
        Assert.False(lease.Acquired);
        // Base backoff is 2s; jitter adds up to another 50%.
        Assert.InRange(lease.RetryAfter, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        time.Advance(lease.RetryAfter + TimeSpan.FromMilliseconds(1));
        Assert.True(sut.TryAcquire(KeyA, TornRequestPriority.Roster).Acquired);
    }

    [Fact]
    public void Consecutive_throttles_escalate_the_backoff()
    {
        var time = new MutableTimeProvider(T0);
        var sut = new TornRateLimiter(time, perMinuteCeiling: 80);

        sut.ReportThrottled(KeyA);
        var first = sut.TryAcquire(KeyA, TornRequestPriority.Roster).RetryAfter;

        time.Advance(first + TimeSpan.FromSeconds(1));
        sut.ReportThrottled(KeyA);
        var second = sut.TryAcquire(KeyA, TornRequestPriority.Roster).RetryAfter;

        // 2s-base doubles to 4s-base; jitter (<=50%) can't close the gap.
        Assert.True(second > first, $"second backoff {second} should exceed first {first}");
    }

    [Fact]
    public void ReportSuccess_outside_the_window_resets_the_escalation()
    {
        var time = new MutableTimeProvider(T0);
        var sut = new TornRateLimiter(time, perMinuteCeiling: 80);

        sut.ReportThrottled(KeyA);
        sut.ReportThrottled(KeyA);
        var escalated = sut.TryAcquire(KeyA, TornRequestPriority.Roster).RetryAfter;
        Assert.True(escalated >= TimeSpan.FromSeconds(4));

        time.Advance(escalated + TimeSpan.FromSeconds(1));
        sut.ReportSuccess(KeyA);

        sut.ReportThrottled(KeyA);
        var afterReset = sut.TryAcquire(KeyA, TornRequestPriority.Roster).RetryAfter;
        // Back to base 2s (+ up to 50% jitter).
        Assert.InRange(afterReset, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void KeyIdentity_is_stable_collision_resistant_and_does_not_expose_the_key()
    {
        var id = TornRateLimiter.KeyIdentity("secret-key-123");

        Assert.Equal(id, TornRateLimiter.KeyIdentity("secret-key-123"));
        Assert.NotEqual(id, TornRateLimiter.KeyIdentity("secret-key-124"));
        Assert.DoesNotContain("secret", id, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(16, id.Length);
        Assert.Throws<ArgumentException>(() => TornRateLimiter.KeyIdentity("   "));
    }

    [Fact]
    public async Task AcquireAsync_blocks_until_a_token_is_available_then_returns()
    {
        // Real clock, 600/min = 10/s so a token returns in ~100ms.
        var sut = new TornRateLimiter(perMinuteCeiling: 600);
        Drain(sut, KeyA, TornRequestPriority.Roster);

        var sw = Stopwatch.StartNew();
        await sut.AcquireAsync(KeyA, TornRequestPriority.Roster, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(40), $"expected to wait for a refill, waited {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task AcquireAsync_throws_when_cancelled_while_waiting()
    {
        var sut = new TornRateLimiter(perMinuteCeiling: 60);
        Drain(sut, KeyA, TornRequestPriority.Other);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.AcquireAsync(KeyA, TornRequestPriority.Other, cts.Token));
    }

    private static void Drain(TornRateLimiter sut, string key, TornRequestPriority priority)
    {
        while (sut.TryAcquire(key, priority).Acquired)
        {
        }
    }

    private static int CountAdmitted(TornRateLimiter sut, string key, TornRequestPriority priority)
    {
        var n = 0;
        while (sut.TryAcquire(key, priority).Acquired)
        {
            n++;
        }

        return n;
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
