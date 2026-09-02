using System.Net;
using System.Text;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using HappyGymStats.WarPoller;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class RankedWarHistoryBackfillFailureTests
{
    private const long FactionId = 111;
    private const string ApiKey = "limited-key-123";

    [Fact]
    public async Task RateLimited_torn_error_is_recorded_with_bounded_backoff_and_retry_count()
    {
        const string rateLimitBody = """
        {
          "error": {
            "code": 5,
            "error": "Rate limit hit for https://api.torn.com/v2/faction/warfareranked?selections=warfareranked&key=limited-key-123"
          }
        }
        """;

        var result = await RunSingleFailingIterationAsync(rateLimitBody, HttpStatusCode.OK);

        Assert.Equal(RankedWarHistoryBackfillFailureCategory.RateLimited.ToString(), result.State.LastFailureCategory);
        Assert.Equal(RankedWarHistoryBackfillStatus.WaitingRetry, result.State.Status);
        Assert.Equal(1, result.State.RetryCount);
        Assert.NotNull(result.State.NextRetryAtUtc);
        Assert.DoesNotContain("key=limited-key-123", result.State.LastErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_http_error_is_classified_separately_from_rate_limiting()
    {
        var result = await RunSingleFailingIterationAsync("service unavailable", HttpStatusCode.ServiceUnavailable, isJson: false);

        Assert.Equal(RankedWarHistoryBackfillFailureCategory.TransientHttp.ToString(), result.State.LastFailureCategory);
        Assert.Equal(RankedWarHistoryBackfillStatus.WaitingRetry, result.State.Status);
    }

    [Fact]
    public async Task Malformed_response_is_classified_as_non_retryable_malformed_response()
    {
        var result = await RunSingleFailingIterationAsync("{not-json", HttpStatusCode.OK, isJson: false);

        Assert.Equal(RankedWarHistoryBackfillFailureCategory.MalformedResponse.ToString(), result.State.LastFailureCategory);
    }

    [Fact]
    public async Task Retry_backoff_grows_with_retry_count_up_to_the_configured_maximum()
    {
        var options = CreateOptions(failureBackoffSeconds: 60, maxFailureBackoffSeconds: 150);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var setupDb = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            await setupDb.Database.EnsureCreatedAsync();
        }

        var handler = new StaticHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("unavailable", Encoding.UTF8, "text/plain")
        });

        var clock = new FixedWarPollerClock(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        // First failure: backoff = 60s (60 * 1).
        var state1 = await RunOneIterationAndReloadStateAsync(connection, handler, options, clock);
        Assert.Equal(1, state1.RetryCount);
        Assert.Equal(clock.UtcNow.AddSeconds(60), state1.NextRetryAtUtc);

        // Force the retry to be due immediately so the second failure is attempted right away.
        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var repo = new RankedWarHistoryBackfillStateRepository(db);
            var state = await repo.GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            state.NextRetryAtUtc = clock.UtcNow;
            await repo.UpsertAsync(state, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        // Second failure: backoff = 120s (60 * 2), still within the 150s cap.
        var state2 = await RunOneIterationAndReloadStateAsync(connection, handler, options, clock);
        Assert.Equal(2, state2.RetryCount);
        Assert.Equal(clock.UtcNow.AddSeconds(120), state2.NextRetryAtUtc);

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var repo = new RankedWarHistoryBackfillStateRepository(db);
            var state = await repo.GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            state.NextRetryAtUtc = clock.UtcNow;
            await repo.UpsertAsync(state, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        // Third failure: backoff would be 180s (60 * 3) but is capped at 150s.
        var state3 = await RunOneIterationAndReloadStateAsync(connection, handler, options, clock);
        Assert.Equal(3, state3.RetryCount);
        Assert.Equal(clock.UtcNow.AddSeconds(150), state3.NextRetryAtUtc);
    }

    [Fact]
    public async Task A_waiting_retry_iteration_makes_no_torn_calls_before_the_retry_time_arrives()
    {
        var options = CreateOptions();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var clock = new FixedWarPollerClock(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            await db.Database.EnsureCreatedAsync();
            await new RankedWarHistoryBackfillStateRepository(db).UpsertAsync(new Data.Entities.RankedWarHistoryBackfillStateEntity
            {
                ScopeKey = options.RankedWarHistoryBackfillScopeKey,
                Status = RankedWarHistoryBackfillStatus.WaitingRetry,
                Phase = RankedWarHistoryBackfillPhase.Idle,
                RetryCount = 1,
                LastFailureCategory = RankedWarHistoryBackfillFailureCategory.TransientHttp.ToString(),
                NextRetryAtUtc = clock.UtcNow.AddSeconds(30),
                CreatedAtUtc = clock.UtcNow,
                UpdatedAtUtc = clock.UtcNow,
            }, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        var handler = new StaticHttpMessageHandler(() => throw new InvalidOperationException("Torn API must not be called before the retry time arrives."));

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, handler, options, clock);
            var result = await worker.RunIterationAsync(CancellationToken.None);

            Assert.Equal(RankedWarHistoryBackfillStatus.WaitingRetry, result.Status);
            Assert.True(result.DelayBeforeNextIteration > TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task Success_after_a_recorded_failure_clears_failure_state_and_resets_retry_count()
    {
        var options = CreateOptions();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var clock = new FixedWarPollerClock(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        var failingHandler = new StaticHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("unavailable", Encoding.UTF8, "text/plain")
        });

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            await db.Database.EnsureCreatedAsync();
        }

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, failingHandler, options, clock);
            await worker.RunIterationAsync(CancellationToken.None);
        }

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var repo = new RankedWarHistoryBackfillStateRepository(db);
            var state = await repo.GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal(RankedWarHistoryBackfillFailureCategory.TransientHttp.ToString(), state.LastFailureCategory);

            // Make the retry due now, so the next iteration actually attempts work.
            state.NextRetryAtUtc = clock.UtcNow;
            await repo.UpsertAsync(state, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        // A zero report budget means the recovery iteration only needs to re-fetch the history page;
        // it never has to fetch a war report, so no report fixture is needed here.
        var recoveryOptions = CreateOptions();
        recoveryOptions.RankedWarHistoryBackfillMaxReportsPerIteration = 0;

        var successHandler = new StaticHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "tests/fixtures/war/v2-warfareranked-page.json")),
                Encoding.UTF8,
                "application/json")
        });

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, successHandler, recoveryOptions, clock);
            await worker.RunIterationAsync(CancellationToken.None);
        }

        await using (var finalDb = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var state = await new RankedWarHistoryBackfillStateRepository(finalDb).GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            Assert.Null(state.LastFailureCategory);
            Assert.Null(state.LastErrorMessage);
            Assert.Null(state.NextRetryAtUtc);
            Assert.Equal(0, state.RetryCount);
            Assert.NotNull(state.LastSuccessAtUtc);
        }
    }

    private static async Task<(RankedWarHistoryBackfillIterationResult Result, Data.Entities.RankedWarHistoryBackfillStateEntity State)> RunSingleFailingIterationAsync(
        string body,
        HttpStatusCode statusCode,
        bool isJson = true)
    {
        var options = CreateOptions();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var setupDb = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            await setupDb.Database.EnsureCreatedAsync();
        }

        var handler = new StaticHttpMessageHandler(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, isJson ? "application/json" : "text/plain")
        });

        var clock = new FixedWarPollerClock(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        RankedWarHistoryBackfillIterationResult result;
        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, handler, options, clock);
            result = await worker.RunIterationAsync(CancellationToken.None);
        }

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var state = await new RankedWarHistoryBackfillStateRepository(db).GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            return (result, state);
        }
    }

    private static async Task<Data.Entities.RankedWarHistoryBackfillStateEntity> RunOneIterationAndReloadStateAsync(
        SqliteConnection connection,
        HttpMessageHandler handler,
        WarPollerOptions options,
        IWarPollerClock clock)
    {
        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, handler, options, clock);
            await worker.RunIterationAsync(CancellationToken.None);
        }

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var state = await new RankedWarHistoryBackfillStateRepository(db).GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            return state;
        }
    }

    private static WarPollerOptions CreateOptions(int failureBackoffSeconds = 60, int maxFailureBackoffSeconds = 900)
        => new()
        {
            ScopeKey = "public-war",
            ApiKey = ApiKey,
            FactionId = FactionId,
            RankedWarHistoryBackfillEnabled = true,
            RankedWarHistoryBackfillMaxPagesPerIteration = 1,
            RankedWarHistoryBackfillMaxReportsPerIteration = 10,
            RankedWarHistoryBackfillFailureBackoffSeconds = failureBackoffSeconds,
            RankedWarHistoryBackfillMaxFailureBackoffSeconds = maxFailureBackoffSeconds,
        };

    private static RankedWarHistoryBackfillWorker CreateWorker(HappyGymStatsDbContext db, HttpMessageHandler handler, WarPollerOptions options, IWarPollerClock clock)
    {
        var tornApiClient = new TornApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.torn.com/") });
        var warHistoryRepository = new WarHistoryRepository(db);
        var ingestWriter = new WarHistoryIngestWriter(warHistoryRepository, db);
        var stateRepository = new RankedWarHistoryBackfillStateRepository(db);

        return new RankedWarHistoryBackfillWorker(
            tornApiClient,
            warHistoryRepository,
            ingestWriter,
            stateRepository,
            db,
            options,
            clock,
            NullLogger<RankedWarHistoryBackfillWorker>.Instance);
    }

    private static DbContextOptions<HappyGymStatsDbContext> CreateContextOptions(SqliteConnection connection)
        => new DbContextOptionsBuilder<HappyGymStatsDbContext>().UseSqlite(connection).Options;

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HappyGymStats.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }

    private sealed class FixedWarPollerClock(DateTimeOffset now) : IWarPollerClock
    {
        public DateTimeOffset UtcNow => now;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StaticHttpMessageHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpResponseMessage Invoke() => responder();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder());
    }
}
