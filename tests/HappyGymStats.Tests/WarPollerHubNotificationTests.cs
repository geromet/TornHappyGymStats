using System.Net;
using System.Text;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using HappyGymStats.WarPoller;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarPollerHubNotificationTests
{
    private const string ApiKey = "limited-key-123";
    private const string ScopeKey = "public-war";
    private const long FactionId = 111;
    private const long WarId = 48377;
    private const string NotifyUrl = "http://127.0.0.1:5000/api/v1/war/internal/notify";

    [Fact]
    public async Task RunOnceAsync_posts_single_notify_only_after_persisted_refresh_and_success_heartbeat()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var tornHandler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request)));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 18, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 18, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 18, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 18, 0, 3, TimeSpan.Zero));

        var notifyHandler = new RecordingHttpMessageHandler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(NotifyUrl, request.RequestUri?.AbsoluteUri);

            var current = await persistence.WarRepository.GetCurrentAsync(ScopeKey, CancellationToken.None);
            Assert.NotNull(current);
            Assert.Equal(WarId, current!.WarId);
            Assert.True(current.IsLive);

            var sample = Assert.Single(await persistence.WarRepository.GetScoreSamplesAsync(WarId, CancellationToken.None));
            Assert.Equal(128, sample.FactionScore);

            var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
            Assert.NotNull(heartbeat);
            Assert.Equal("succeeded", heartbeat!.Phase);
            Assert.Equal(WarId, heartbeat.ActiveWarId);
            Assert.Equal(new DateTimeOffset(2026, 5, 9, 18, 0, 3, TimeSpan.Zero), heartbeat.PollCompletedAtUtc);

            return JsonResponse("{}", HttpStatusCode.Accepted);
        });

        var sut = CreateSut(persistence, tornHandler, notifyHandler, clock);

        var result = await sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal("succeeded", result.Phase);
        Assert.Equal(WarId, result.ActiveWarId);
        Assert.True(result.PersistedWarState);
        // Four since M008 added the chain-deadline call; see WarPollerServiceTests for why
        // this number is asserted rather than ignored.
        Assert.Equal(4, tornHandler.Requests.Count);
        Assert.Single(notifyHandler.Requests);
    }

    [Fact]
    public async Task RunOnceAsync_notify_failure_is_non_fatal_and_does_not_trigger_extra_torn_fetches()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var tornHandler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request)));
        var clock = new RecordingWarPollerClock(
            new DateTimeOffset(2026, 5, 9, 19, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 19, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 19, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 9, 19, 0, 3, TimeSpan.Zero));

        var notifyHandler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal(NotifyUrl, request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse("{\"status\":\"error\"}", HttpStatusCode.InternalServerError));
        });

        var sut = CreateSut(persistence, tornHandler, notifyHandler, clock);

        var result = await sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal("succeeded", result.Phase);
        Assert.Equal(WarId, result.ActiveWarId);
        Assert.True(result.PersistedWarState);
        // Four since M008 added the chain-deadline call; see WarPollerServiceTests for why
        // this number is asserted rather than ignored.
        Assert.Equal(4, tornHandler.Requests.Count);
        Assert.Single(notifyHandler.Requests);

        var current = await persistence.WarRepository.GetCurrentAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(WarId, current!.WarId);

        var heartbeat = await persistence.WarRepository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
        Assert.NotNull(heartbeat);
        Assert.Equal("succeeded", heartbeat!.Phase);
        Assert.Equal(WarId, heartbeat.ActiveWarId);
    }

    [Fact]
    public async Task Constructor_rejects_non_loopback_hub_notify_url()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var options = new WarPollerOptions
        {
            ScopeKey = ScopeKey,
            ApiKey = ApiKey,
            FactionId = FactionId,
            PollIntervalSeconds = 30,
            FailureBackoffSeconds = 60,
            MaxFailureBackoffSeconds = 300,
            StaleThresholdSeconds = 120,
            HubNotifyUrl = "https://example.com/api/v1/war/internal/notify",
            HubNotifyTimeoutSeconds = 5
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new WarPollerService(
            new TornApiClient(new HttpClient(new RecordingHttpMessageHandler((request, _) => Task.FromResult(RouteWarResponse(request)))) { BaseAddress = new Uri("https://api.torn.com/") }),
            persistence.WarRepository,
            persistence.ImportRunRepository,
            persistence.UnitOfWork,
            options,
            new NoOpWarPollerNotifier(),
            new RecordingWarPollerClock(new DateTimeOffset(2026, 5, 9, 20, 0, 0, TimeSpan.Zero)),
            NullLogger<WarPollerService>.Instance));

        Assert.Contains("loopback host", ex.Message, StringComparison.Ordinal);
    }

    private static WarPollerService CreateSut(
        TestPersistenceScope persistence,
        HttpMessageHandler tornHandler,
        HttpMessageHandler notifyHandler,
        RecordingWarPollerClock clock)
    {
        var options = new WarPollerOptions
        {
            ScopeKey = ScopeKey,
            ApiKey = ApiKey,
            FactionId = FactionId,
            PollIntervalSeconds = 30,
            FailureBackoffSeconds = 60,
            MaxFailureBackoffSeconds = 300,
            StaleThresholdSeconds = 120,
            HubNotifyUrl = NotifyUrl,
            HubNotifyTimeoutSeconds = 5
        };

        return new WarPollerService(
            new TornApiClient(new HttpClient(tornHandler) { BaseAddress = new Uri("https://api.torn.com/") }),
            persistence.WarRepository,
            persistence.ImportRunRepository,
            persistence.UnitOfWork,
            options,
            new WarPollerNotifier(new HttpClient(notifyHandler), options, NullLogger<WarPollerNotifier>.Instance),
            clock,
            NullLogger<WarPollerService>.Instance);
    }

    private static HttpResponseMessage RouteWarResponse(HttpRequestMessage request)
    {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;

        // M008: our faction's chain deadline, one WarState call per tick.
        if (uri == $"https://api.torn.com/v2/faction?selections=chain&key={ApiKey}")
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/faction-chain-live.json"));
        }

        if (uri == $"https://api.torn.com/faction/?selections=rankedwars&key={ApiKey}")
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/live-faction-wars.json"));
        }

        if (uri == $"https://api.torn.com/torn/?selections=rankedwars&key={ApiKey}")
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/global-ranked-wars-live.json"));
        }

        if (uri == $"https://api.torn.com/torn/48377?selections=rankedwarreport&key={ApiKey}")
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/ranked-war-report-48377.json"));
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

    private sealed class NoOpWarPollerNotifier : IWarPollerNotifier
    {
        public Task NotifyWarStateUpdatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

    private sealed class TestPersistenceScope : IAsyncDisposable
    {
        private TestPersistenceScope(SqliteConnection connection, HappyGymStatsDbContext dbContext)
        {
            Connection = connection;
            DbContext = dbContext;
            WarRepository = new WarStateRepository(dbContext);
            ImportRunRepository = new ImportRunRepository(dbContext);
            UnitOfWork = dbContext;
        }

        public SqliteConnection Connection { get; }
        public HappyGymStatsDbContext DbContext { get; }
        public IWarStateRepository WarRepository { get; }
        public IImportRunRepository ImportRunRepository { get; }
        public IUnitOfWork UnitOfWork { get; }

        public static async Task<TestPersistenceScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new HappyGymStatsDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new TestPersistenceScope(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
