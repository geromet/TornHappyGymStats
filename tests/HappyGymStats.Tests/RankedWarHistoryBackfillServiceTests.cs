using System.Diagnostics;
using System.Net;
using System.Text;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using HappyGymStats.WarPoller;
using WarPollerProgram = HappyGymStats.WarPoller.Program;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class RankedWarHistoryBackfillServiceTests
{
    private const long FactionId = 111;
    private const string ApiKey = "limited-key-123";

    [Fact]
    public void BuildHost_registers_the_backfill_worker_and_hosted_service()
    {
        using var host = WarPollerProgram.BuildHost(
            configureBuilder: builder => builder.Configuration.AddInMemoryCollection(CreateConfiguration(enabled: false)));

        using var scope = host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RankedWarHistoryBackfillWorker>());
        Assert.Single(host.Services.GetServices<IHostedService>().OfType<RankedWarHistoryBackfillHostedService>());
    }

    [Fact]
    public void Enabled_options_with_invalid_limits_fail_validation()
    {
        var options = new WarPollerOptions
        {
            ScopeKey = "public-war",
            ApiKey = ApiKey,
            FactionId = FactionId,
            RankedWarHistoryBackfillEnabled = true,
            RankedWarHistoryBackfillMaxPagesPerIteration = 0,
        };

        Assert.Throws<InvalidOperationException>(() => WarPollerProgram.BuildHost(
            configureBuilder: builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HappyGymStats"] = "Host=localhost;Database=happygymstats_test;Username=test;Password=test",
                ["WarPoller:ApiKey"] = ApiKey,
                ["WarPoller:FactionId"] = FactionId.ToString(),
                ["WarPoller:PollIntervalSeconds"] = "300",
                ["WarPoller:FailureBackoffSeconds"] = "60",
                ["WarPoller:MaxFailureBackoffSeconds"] = "300",
                ["WarPoller:StaleThresholdSeconds"] = "600",
                ["WarPoller:RankedWarHistoryBackfillEnabled"] = "true",
                ["WarPoller:RankedWarHistoryBackfillMaxPagesPerIteration"] = "0",
            }))
            .Services.GetRequiredService<WarPollerOptions>());
    }

    [Fact]
    public async Task Disabled_hosted_service_makes_no_torn_calls_or_database_writes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await EnsureCreatedAsync(connection);

        var handler = new RecordingHttpMessageHandler((_, _) => throw new InvalidOperationException("Torn API must not be called while backfill is disabled."));
        var hostedService = CreateHostedService(connection, handler, enabled: false, out _);

        await hostedService.StartAsync(CancellationToken.None);
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(stopCts.Token);

        await using var db = new HappyGymStatsDbContext(CreateContextOptions(connection));
        Assert.Equal(0, await db.RankedWarHistoryBackfillState.CountAsync());
        Assert.Equal(0, await db.RankedWarHistory.CountAsync());
    }

    [Fact]
    public async Task First_run_persists_history_and_reports_second_run_resumes_and_skips_captured_reports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await EnsureCreatedAsync(connection);

        var callCounts = new Dictionary<string, int>();
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            callCounts[uri] = callCounts.GetValueOrDefault(uri) + 1;
            return Task.FromResult(RouteBackfillResponse(uri));
        });

        var options = new WarPollerOptions
        {
            ScopeKey = "public-war",
            ApiKey = ApiKey,
            FactionId = FactionId,
            RankedWarHistoryBackfillEnabled = true,
            RankedWarHistoryBackfillMaxPagesPerIteration = 1,
            RankedWarHistoryBackfillMaxReportsPerIteration = 1,
        };

        // Iteration 1: fetch page 1 (two wars), budget only covers one report -> page not drained.
        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, handler, options);
            var result1 = await worker.RunIterationAsync(CancellationToken.None);
            Assert.Equal(RankedWarHistoryBackfillStatus.Running, result1.Status);
        }

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var state = await new RankedWarHistoryBackfillStateRepository(db).GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            Assert.Null(state.NextHistoryPageUrl);
            Assert.Equal(1, state.ReportsProcessed);
            Assert.Equal(0, state.PagesProcessed);

            var war48377 = await db.RankedWarHistory.SingleAsync(w => w.WarId == 48377);
            Assert.True(war48377.ReportCapturedAtUtc.HasValue, "war 48377 should have a captured report after iteration 1");
        }

        // Iteration 2: re-fetches page 1 (report for war 48377 already captured, skipped), fetches the
        // remaining report for war 48360, drains the page, and advances the cursor to page 2. This also
        // guards against re-ingesting a history page wiping out a previously captured report's timestamps.
        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, handler, options);
            await worker.RunIterationAsync(CancellationToken.None);
        }

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var state = await new RankedWarHistoryBackfillStateRepository(db).GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            Assert.Null(state.LastFailureCategory);
            Assert.Equal("/v2/faction/warfareranked?selections=warfareranked&page=2", state.NextHistoryPageUrl);
            Assert.Equal(1, state.PagesProcessed);
            Assert.Equal(2, state.ReportsProcessed);
        }

        // Iteration 3: fetches the (empty, no-next) page 2 and marks the backfill complete.
        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            var worker = CreateWorker(db, handler, options);
            var result3 = await worker.RunIterationAsync(CancellationToken.None);
            Assert.Equal(RankedWarHistoryBackfillStatus.Completed, result3.Status);
        }

        await using (var db = new HappyGymStatsDbContext(CreateContextOptions(connection)))
        {
            Assert.Equal(2, await db.RankedWarHistory.CountAsync());
            Assert.True(await db.RankedWarReportMembers.AnyAsync(m => m.WarId == 48377));
            Assert.True(await db.RankedWarReportMembers.AnyAsync(m => m.WarId == 48360));

            var state = await new RankedWarHistoryBackfillStateRepository(db).GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal(RankedWarHistoryBackfillStatus.Completed, state.Status);
            Assert.Equal(2, state.PagesProcessed);
        }

        Assert.Equal(1, callCounts.Single(kv => kv.Key.Contains("torn/48377", StringComparison.Ordinal)).Value);
        Assert.Equal(1, callCounts.Single(kv => kv.Key.Contains("torn/48360", StringComparison.Ordinal)).Value);
    }

    [Fact]
    public async Task HostedService_stops_promptly_when_cancellation_is_requested()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await EnsureCreatedAsync(connection);

        var clock = new BlockingWarPollerClock(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteBackfillResponse(request.RequestUri?.AbsoluteUri ?? string.Empty)));
        var hostedService = CreateHostedService(connection, handler, enabled: true, out _, clock);

        await hostedService.StartAsync(CancellationToken.None);
        Assert.True(await clock.WaitForDelayAsync(TimeSpan.FromSeconds(10)), "Timed out waiting for hosted service to enter its delay loop.");

        var stopwatch = Stopwatch.StartNew();
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(stopCts.Token);
        stopwatch.Stop();

        Assert.True(clock.CancellationObserved);
        Assert.False(stopCts.IsCancellationRequested, $"Hosted service stop exceeded the 5 second cancellation timeout; observed {stopwatch.Elapsed}.");
    }

    private static RankedWarHistoryBackfillWorker CreateWorker(HappyGymStatsDbContext db, HttpMessageHandler handler, WarPollerOptions options)
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
            TimeProvider.System,
            NullLogger<RankedWarHistoryBackfillWorker>.Instance);
    }

    private static RankedWarHistoryBackfillHostedService CreateHostedService(
        SqliteConnection connection,
        HttpMessageHandler handler,
        bool enabled,
        out WarPollerOptions options,
        TimeProvider? clock = null)
    {
        options = new WarPollerOptions
        {
            ScopeKey = "public-war",
            ApiKey = ApiKey,
            FactionId = FactionId,
            RankedWarHistoryBackfillEnabled = enabled,
            RankedWarHistoryBackfillMaxPagesPerIteration = 1,
            RankedWarHistoryBackfillMaxReportsPerIteration = 10,
        };

        var effectiveClock = clock ?? TimeProvider.System;
        var db = new HappyGymStatsDbContext(CreateContextOptions(connection));
        var worker = CreateWorker(db, handler, options);

        var services = new ServiceCollection()
            .AddSingleton(worker)
            .BuildServiceProvider();

        return new RankedWarHistoryBackfillHostedService(
            new SingleServiceScopeFactory(services),
            options,
            effectiveClock,
            NullLogger<RankedWarHistoryBackfillHostedService>.Instance);
    }

    private static async Task EnsureCreatedAsync(SqliteConnection connection)
    {
        await using var db = new HappyGymStatsDbContext(CreateContextOptions(connection));
        await db.Database.EnsureCreatedAsync();
    }

    private static DbContextOptions<HappyGymStatsDbContext> CreateContextOptions(SqliteConnection connection)
        => new DbContextOptionsBuilder<HappyGymStatsDbContext>().UseSqlite(connection).Options;

    private static Dictionary<string, string?> CreateConfiguration(bool enabled)
        => new()
        {
            ["ConnectionStrings:HappyGymStats"] = "Host=localhost;Database=happygymstats_test;Username=test;Password=test",
            ["WarPoller:ApiKey"] = ApiKey,
            ["WarPoller:FactionId"] = FactionId.ToString(),
            ["WarPoller:PollIntervalSeconds"] = "300",
            ["WarPoller:FailureBackoffSeconds"] = "60",
            ["WarPoller:MaxFailureBackoffSeconds"] = "300",
            ["WarPoller:StaleThresholdSeconds"] = "600",
            ["WarPoller:RankedWarHistoryBackfillEnabled"] = enabled ? "true" : "false",
        };

    private static HttpResponseMessage RouteBackfillResponse(string uri)
    {
        if (uri.StartsWith("https://api.torn.com/v2/faction/warfareranked?selections=warfareranked&page=2", StringComparison.Ordinal))
        {
            return JsonResponse(EmptySecondPageJson);
        }

        if (uri.StartsWith("https://api.torn.com/v2/faction/warfareranked?selections=warfareranked", StringComparison.Ordinal))
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/v2-warfareranked-page.json"));
        }

        if (uri.Contains("torn/48377?selections=rankedwarreport", StringComparison.Ordinal))
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/v2-ranked-war-report-48377.json"));
        }

        if (uri.Contains("torn/48360?selections=rankedwarreport", StringComparison.Ordinal))
        {
            return JsonResponse(SecondWarReportJson);
        }

        throw new InvalidOperationException($"Unexpected request URI: {uri}");
    }

    private const string EmptySecondPageJson = """
    {
      "wars": [],
      "_metadata": {
        "links": { "next": null, "prev": "/v2/faction/warfareranked?selections=warfareranked&page=1" },
        "has_more": false
      }
    }
    """;

    private const string SecondWarReportJson = """
    {
      "war": {
        "war_id": 48360,
        "start": 1730800000,
        "end": 1730807200,
        "is_live": false,
        "winner_faction_id": 666,
        "status": "finished"
      },
      "factions": [
        {
          "faction_id": 555,
          "name": "Night Guard",
          "score": 97,
          "chain": 31,
          "attacks": 10,
          "members": [
            { "user_id": 2001, "name": "Nyx", "score": 50, "chain": 15, "attacks": 5, "status": { "state": "okay", "until": null } }
          ]
        },
        {
          "faction_id": 666,
          "name": "Morning Riot",
          "score": 100,
          "chain": 34,
          "attacks": 9,
          "members": [
            { "user_id": 3001, "name": "Rook", "score": 60, "chain": 18, "attacks": 6, "status": { "state": "okay", "until": null } }
          ]
        }
      ],
      "idle_attackers": []
    }
    """;

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static string ReadFixture(string relativePath)
        => File.ReadAllText(Path.Combine(ProjectRoot, relativePath));

    private static string ProjectRoot => ResolveRepositoryRoot();

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

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }

    private sealed class BlockingWarPollerClock(DateTimeOffset now) : TimeProvider
    {
        private readonly TaskCompletionSource _delayEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DateTimeOffset GetUtcNow() => now;

        public bool CancellationObserved { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _delayEntered.TrySetResult();
            return new ObservedTimer(
                TimeProvider.System.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan),
                () => CancellationObserved = true);
        }

        public async Task<bool> WaitForDelayAsync(TimeSpan timeout)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            try
            {
                await _delayEntered.Task.WaitAsync(timeoutCts.Token);
                return true;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return false;
            }
        }

        private sealed class ObservedTimer(ITimer inner, Action onDispose) : ITimer
        {
            private readonly ITimer _inner = inner;
            private readonly Action _onDispose = onDispose;
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
                => _inner.Change(dueTime, period);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _onDispose();
                _inner.Dispose();
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _onDispose();
                await _inner.DisposeAsync();
            }
        }
    }

    private sealed class SingleServiceScopeFactory(IServiceProvider services) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new SingleServiceScope(services);
    }

    private sealed class SingleServiceScope(IServiceProvider services) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = services;

        public void Dispose()
        {
        }
    }
}
