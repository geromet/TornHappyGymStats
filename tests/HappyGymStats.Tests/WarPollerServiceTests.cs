using System.Net;
using System.Text;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using HappyGymStats.WarPoller;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarPollerServiceTests
{
    private const string ApiKey = "limited-key-123";
    private const string ScopeKey = "public-war";
    private const long FactionId = 111;
    private const long WarId = 48377;

    [Fact]
    public async Task RunOnceAsync_persists_current_war_roster_score_sample_and_heartbeat()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request)));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 12, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 12, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 12, 0, 3, TimeSpan.Zero));

        var sut = CreateSut(persistence, handler, clock);

        var result = await sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal("succeeded", result.Phase);
        Assert.Equal(WarId, result.ActiveWarId);
        Assert.True(result.PersistedWarState);
        Assert.Equal(TimeSpan.FromSeconds(30), result.DelayBeforeNextTick);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(handler.Requests, request => request.RequestUri?.AbsoluteUri == $"https://api.torn.com/faction/?selections=rankedwars&key={ApiKey}");
        Assert.Contains(handler.Requests, request => request.RequestUri?.AbsoluteUri == $"https://api.torn.com/torn/?selections=rankedwars&key={ApiKey}");
        Assert.Contains(handler.Requests, request => request.RequestUri?.AbsoluteUri == $"https://api.torn.com/torn/48377?selections=rankedwarreport&key={ApiKey}");

        var current = await persistence.WarRepository.GetCurrentAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(WarId, current!.WarId);
        Assert.Equal(FactionId, current.FactionId);
        Assert.Equal("Happy Gym", current.FactionName);
        Assert.Equal(222, current.OpponentFactionId);
        Assert.Equal("Chain Breakers", current.OpponentFactionName);
        Assert.True(current.IsLive);
        Assert.Equal(new DateTimeOffset(2026, 5, 9, 12, 0, 2, TimeSpan.Zero), current.ObservedAtUtc);

        var roster = await persistence.WarRepository.GetRosterSnapshotAsync(WarId, CancellationToken.None);
        Assert.Equal(5, roster.Count);
        Assert.Contains(roster, row => row.MemberId == 1001 && row.StatusState == "hospital");
        Assert.Contains(roster, row => row.MemberId == 2002 && row.StatusState == "travel" && row.StatusUntilUtc is null);
        Assert.All(roster, row => Assert.Equal(new DateTimeOffset(2026, 5, 9, 12, 0, 2, TimeSpan.Zero), row.CapturedAtUtc));

        var sample = Assert.Single(await persistence.WarRepository.GetScoreSamplesAsync(WarId, CancellationToken.None));
        Assert.Equal(128, sample.FactionScore);
        Assert.Equal(42, sample.FactionChain);
        Assert.Equal(117, sample.OpponentScore);
        Assert.Equal(39, sample.OpponentChain);
        Assert.Equal(new DateTimeOffset(2026, 5, 9, 12, 0, 2, TimeSpan.Zero), sample.SampledAtUtc);

        var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(heartbeat);
        Assert.Equal("succeeded", heartbeat!.Phase);
        Assert.Equal(WarId, heartbeat.ActiveWarId);
        Assert.Equal(0, heartbeat.RetryCount);
        Assert.Null(heartbeat.LastError);
        Assert.Equal(new DateTimeOffset(2026, 5, 9, 12, 0, 1, TimeSpan.Zero), heartbeat.PollStartedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 5, 9, 12, 0, 3, TimeSpan.Zero), heartbeat.PollCompletedAtUtc);
    }

    [Fact]
    public async Task RunOnceAsync_without_active_war_clears_current_war_and_skips_report_fetch()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request, liveBody: "{\"wars\":[]}", globalBody: "{\"wars\":[]}")));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 13, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 13, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 13, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 13, 0, 3, TimeSpan.Zero));

        var sut = CreateSut(persistence, handler, clock);

        var result = await sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal("succeeded", result.Phase);
        Assert.Null(result.ActiveWarId);
        Assert.False(result.PersistedWarState);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri?.AbsoluteUri?.Contains("rankedwarreport", StringComparison.Ordinal) == true);

        var current = await persistence.WarRepository.GetCurrentAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Null(current!.WarId);
        Assert.False(current.IsLive);
        Assert.Equal(FactionId, current.FactionId);

        Assert.Empty(await persistence.WarRepository.GetScoreSamplesAsync(WarId, CancellationToken.None));

        var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(heartbeat);
        Assert.Equal("succeeded", heartbeat!.Phase);
        Assert.Null(heartbeat.ActiveWarId);
    }

    [Fact]
    public async Task RunOnceAsync_records_retryable_failure_with_bounded_backoff_and_redacted_error()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        await persistence.WarRepository.UpsertHeartbeatAsync(
            new WarPollerHeartbeatEntity
            {
                ScopeKey = ScopeKey,
                Phase = "retryable-failure",
                UpdatedAtUtc = new DateTimeOffset(2026, 5, 9, 11, 59, 0, TimeSpan.Zero),
                RetryCount = 4,
                ActiveWarId = WarId,
                PollIntervalSeconds = 30,
                FailureBackoffSeconds = 120
            },
            CancellationToken.None);
        await persistence.DbContext.SaveChangesAsync();

        const string retryBody = """
        {
          "error": {
            "code": 5,
            "error": "Rate limit hit for https://api.torn.com/torn/?selections=rankedwars&key=caller-secret"
          }
        }
        """;

        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request, liveBody: retryBody)));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 14, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 14, 0, 2, TimeSpan.Zero));

        var sut = CreateSut(
            persistence,
            handler,
            clock,
            new WarPollerOptions
            {
                ScopeKey = ScopeKey,
                ApiKey = ApiKey,
                FactionId = FactionId,
                PollIntervalSeconds = 30,
                FailureBackoffSeconds = 90,
                MaxFailureBackoffSeconds = 120
            });

        var result = await sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal("retryable-failure", result.Phase);
        Assert.Equal(WarId, result.ActiveWarId);
        Assert.Equal(TimeSpan.FromSeconds(120), result.DelayBeforeNextTick);
        Assert.Equal(new[] { TimeSpan.FromSeconds(120) }, clock.Delays);

        var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(heartbeat);
        Assert.Equal("retryable-failure", heartbeat!.Phase);
        Assert.Equal(5, heartbeat.RetryCount);
        Assert.Equal(120, heartbeat.FailureBackoffSeconds);
        Assert.Contains("TornApiException", heartbeat.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("caller-secret", heartbeat.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("https://api.torn.com", heartbeat.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunOnceAsync_rejects_malformed_report_and_records_failed_heartbeat()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        const string malformedReportBody = """
        {
          "war": {
            "war_id": 48377,
            "name": "Broken",
            "start": 1731000000,
            "end": null,
            "is_live": true
          },
          "factions": [
            {
              "faction_id": 111,
              "name": "Happy Gym",
              "score": 128,
              "chain": 42,
              "members": []
            }
          ],
          "idle_attackers": []
        }
        """;

        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request, reportBody: malformedReportBody)));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 15, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 15, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 15, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 15, 0, 3, TimeSpan.Zero));

        var sut = CreateSut(persistence, handler, clock);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => sut.RunOnceAsync(CancellationToken.None));

        Assert.Contains("exactly two factions", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await persistence.WarRepository.GetScoreSamplesAsync(WarId, CancellationToken.None));

        var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(heartbeat);
        Assert.Equal("failed", heartbeat!.Phase);
        Assert.Equal(WarId, heartbeat.ActiveWarId);
        Assert.Contains("InvalidDataException", heartbeat.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunOnceAsync_records_cancelled_heartbeat_before_persistence_when_token_is_cancelled()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync(cancelAfterSaveCall: 2);
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request)));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 16, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 16, 0, 2, TimeSpan.Zero));

        var sut = CreateSut(persistence, handler, clock);

        var result = await sut.RunOnceAsync(persistence.CancellationSource.Token);

        Assert.Equal("cancelled", result.Phase);
        Assert.Null(result.ActiveWarId);
        Assert.Empty(handler.Requests);
        Assert.Null(await persistence.WarRepository.GetCurrentAsync(ScopeKey, CancellationToken.None));

        var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(heartbeat);
        Assert.Equal("cancelled", heartbeat!.Phase);
        Assert.Null(heartbeat.ActiveWarId);
    }

    [Fact]
    public async Task RunOnceAsync_records_cancelled_heartbeat_after_persistence_when_token_is_cancelled()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync(cancelAfterSaveCall: 3);
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request)));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 17, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 17, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 17, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 17, 0, 3, TimeSpan.Zero));

        var sut = CreateSut(persistence, handler, clock);

        var result = await sut.RunOnceAsync(persistence.CancellationSource.Token);

        Assert.Equal("cancelled", result.Phase);
        Assert.Equal(WarId, result.ActiveWarId);

        var current = await persistence.WarRepository.GetCurrentAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(WarId, current!.WarId);

        var sample = Assert.Single(await persistence.WarRepository.GetScoreSamplesAsync(WarId, CancellationToken.None));
        Assert.Equal(128, sample.FactionScore);

        var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(heartbeat);
        Assert.Equal("cancelled", heartbeat!.Phase);
        Assert.Equal(WarId, heartbeat.ActiveWarId);
    }

    private static WarPollerService CreateSut(
        TestPersistenceScope persistence,
        HttpMessageHandler handler,
        RecordingWarPollerClock clock,
        WarPollerOptions? options = null)
        => new(
            new TornApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.torn.com/") }),
            persistence.WarRepository,
            persistence.ImportRunRepository,
            persistence.UnitOfWork,
            options ?? new WarPollerOptions { ScopeKey = ScopeKey, ApiKey = ApiKey, FactionId = FactionId, PollIntervalSeconds = 30, FailureBackoffSeconds = 60, MaxFailureBackoffSeconds = 300 },
            new NoOpWarPollerNotifier(),
            clock,
            NullLogger<WarPollerService>.Instance);

    private static HttpResponseMessage RouteWarResponse(HttpRequestMessage request, string? liveBody = null, string? globalBody = null, string? reportBody = null)
    {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
        if (uri == $"https://api.torn.com/faction/?selections=rankedwars&key={ApiKey}")
        {
            return JsonResponse(liveBody ?? ReadFixture("tests/fixtures/war/live-faction-wars.json"));
        }

        if (uri == $"https://api.torn.com/torn/?selections=rankedwars&key={ApiKey}")
        {
            return JsonResponse(globalBody ?? ReadFixture("tests/fixtures/war/global-ranked-wars-live.json"));
        }

        if (uri == $"https://api.torn.com/torn/48377?selections=rankedwarreport&key={ApiKey}")
        {
            return JsonResponse(reportBody ?? ReadFixture("tests/fixtures/war/ranked-war-report-48377.json"));
        }

        throw new Xunit.Sdk.XunitException($"Unexpected Torn request: {uri}");
    }

    private static HttpResponseMessage JsonResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static string ReadFixture(string relativePath)
        => File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), relativePath));

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

    private sealed class RecordingWarPollerClock(params DateTimeOffset[] timestamps) : IWarPollerClock
    {
        private readonly Queue<DateTimeOffset> _timestamps = new(timestamps);
        private DateTimeOffset _last = timestamps.Length > 0 ? timestamps[^1] : DateTimeOffset.UtcNow;

        public List<TimeSpan> Delays { get; } = [];

        public DateTimeOffset UtcNow
        {
            get
            {
                if (_timestamps.Count > 0)
                {
                    _last = _timestamps.Dequeue();
                }

                return _last;
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _responder(request, cancellationToken);
        }
    }

    private sealed class NoOpWarPollerNotifier : IWarPollerNotifier
    {
        public Task NotifyWarStateUpdatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CancellingUnitOfWork(HappyGymStatsDbContext dbContext, CancellationTokenSource cancellationSource, int cancelAfterSaveCall) : IUnitOfWork
    {
        private int _saveCount;

        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            var result = await dbContext.SaveChangesAsync(ct);
            _saveCount++;
            if (_saveCount == cancelAfterSaveCall)
            {
                cancellationSource.Cancel();
            }

            return result;
        }
    }

    private sealed class TestPersistenceScope : IAsyncDisposable
    {
        private TestPersistenceScope(SqliteConnection connection, HappyGymStatsDbContext dbContext, IUnitOfWork unitOfWork, CancellationTokenSource cancellationSource)
        {
            Connection = connection;
            DbContext = dbContext;
            WarRepository = new WarStateRepository(dbContext);
            ImportRunRepository = new ImportRunRepository(dbContext);
            UnitOfWork = unitOfWork;
            CancellationSource = cancellationSource;
        }

        public SqliteConnection Connection { get; }
        public HappyGymStatsDbContext DbContext { get; }
        public IWarStateRepository WarRepository { get; }
        public IImportRunRepository ImportRunRepository { get; }
        public IUnitOfWork UnitOfWork { get; }
        public CancellationTokenSource CancellationSource { get; }

        public static async Task<TestPersistenceScope> CreateAsync(int? cancelAfterSaveCall = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new HappyGymStatsDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var cts = new CancellationTokenSource();
            IUnitOfWork unitOfWork = cancelAfterSaveCall is int value
                ? new CancellingUnitOfWork(dbContext, cts, value)
                : dbContext;

            return new TestPersistenceScope(connection, dbContext, unitOfWork, cts);
        }

        public async ValueTask DisposeAsync()
        {
            CancellationSource.Dispose();
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
