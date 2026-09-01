using System.Diagnostics;
using System.Net;
using System.Text;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using HappyGymStats.WarPoller;
using WarPollerProgram = HappyGymStats.WarPoller.Program;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarPollerHostTests
{
    private const long FactionId = 111;

    [Fact]
    public void BuildHost_registers_worker_services_without_web_listener_dependencies()
    {
        using var host = WarPollerProgram.BuildHost(
            configureBuilder: builder => builder.Configuration.AddInMemoryCollection(CreateConfiguration()));

        using var scope = host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TornApiClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WarPollerService>());
        Assert.IsType<HappyGymStatsDbContext>(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.Single(host.Services.GetServices<IHostedService>().OfType<WarPollerHostedService>());
    }

    [Fact]
    public void Worker_source_contains_generic_host_wiring_only()
    {
        var projectFile = File.ReadAllText(Path.Combine(ProjectRoot, "src/HappyGymStats.WarPoller/HappyGymStats.WarPoller.csproj"));
        Assert.DoesNotContain("Microsoft.NET.Sdk.Web", projectFile, StringComparison.Ordinal);

        var programSource = File.ReadAllText(Path.Combine(ProjectRoot, "src/HappyGymStats.WarPoller/Program.cs"));
        var hostedServiceSource = File.ReadAllText(Path.Combine(ProjectRoot, "src/HappyGymStats.WarPoller/WarPollerHostedService.cs"));

        Assert.Contains("Host.CreateApplicationBuilder", programSource, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient<TornApiClient>", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new TornApiClient(", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WebApplication", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Kestrel", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WebApplication", hostedServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Kestrel", hostedServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLiveFactionWarsAsync", hostedServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetGlobalRankedWarsAsync", hostedServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRankedWarReportAsync", hostedServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostedService_stops_promptly_when_cancellation_is_requested()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var db = new HappyGymStatsDbContext(
                         new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                             .UseSqlite(connection)
                             .Options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var clock = new BlockingWarPollerClock(new DateTimeOffset(2026, 5, 9, 17, 0, 0, TimeSpan.Zero));

        const string retryBody = """
        {
          "error": {
            "code": 5,
            "error": "Rate limit hit for https://api.torn.com/faction/?selections=rankedwars&key=caller-secret"
          }
        }
        """;

        await using var scopedDb = new HappyGymStatsDbContext(
            new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite(connection)
                .Options);

        var poller = new WarPollerService(
            new TornApiClient(new HttpClient(new RecordingHttpMessageHandler((request, _) =>
            {
                var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
                if (uri == "https://api.torn.com/faction/?selections=rankedwars&key=limited-key-123")
                {
                    return Task.FromResult(JsonResponse(retryBody));
                }

                throw new InvalidOperationException($"Unexpected request URI: {uri}");
            }))
            {
                BaseAddress = new Uri("https://api.torn.com/")
            }),
            new WarStateRepository(scopedDb),
            new ImportRunRepository(scopedDb),
            scopedDb,
            new WarPollerOptions
            {
                ScopeKey = "public-war",
                ApiKey = "limited-key-123",
                FactionId = FactionId,
                PollIntervalSeconds = 300,
                FailureBackoffSeconds = 60,
                MaxFailureBackoffSeconds = 300,
                StaleThresholdSeconds = 600
            },
            clock,
            NullLogger<WarPollerService>.Instance);

        using var services = new ServiceCollection()
            .AddSingleton(poller)
            .BuildServiceProvider();

        var hostedService = new WarPollerHostedService(
            new SingleServiceScopeFactory(services),
            clock,
            NullLogger<WarPollerHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        Assert.True(await clock.WaitForDelayAsync(TimeSpan.FromSeconds(10)), "Timed out waiting for hosted service to enter its delay loop.");

        var stopwatch = Stopwatch.StartNew();
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(stopCts.Token);
        stopwatch.Stop();

        Assert.True(clock.CancellationObserved);
        Assert.False(stopCts.IsCancellationRequested, $"Hosted service stop exceeded the 5 second cancellation timeout; observed {stopwatch.Elapsed}.");
    }

    private static Dictionary<string, string?> CreateConfiguration()
        => new()
        {
            ["ConnectionStrings:HappyGymStats"] = "Host=localhost;Database=happygymstats_test;Username=test;Password=test",
            ["WarPoller:ApiKey"] = "limited-key-123",
            ["WarPoller:FactionId"] = FactionId.ToString(),
            ["WarPoller:PollIntervalSeconds"] = "300",
            ["WarPoller:FailureBackoffSeconds"] = "60",
            ["WarPoller:MaxFailureBackoffSeconds"] = "300",
            ["WarPoller:StaleThresholdSeconds"] = "600"
        };

    private static HttpResponseMessage RouteWarResponse(HttpRequestMessage request)
    {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;

        if (uri == "https://api.torn.com/faction/?selections=rankedwars&key=limited-key-123")
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/live-faction-wars.json"));
        }

        if (uri == "https://api.torn.com/torn/?selections=rankedwars&key=limited-key-123")
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/global-ranked-wars-live.json"));
        }

        if (uri == "https://api.torn.com/torn/48377?selections=rankedwarreport&key=limited-key-123")
        {
            return JsonResponse(ReadFixture("tests/fixtures/war/ranked-war-report-48377.json"));
        }

        throw new InvalidOperationException($"Unexpected request URI: {uri}");
    }

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static string ReadFixture(string relativePath)
        => File.ReadAllText(Path.Combine(ProjectRoot, relativePath));

    private static string ProjectRoot
        => ResolveRepositoryRoot();

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

    private sealed class BlockingWarPollerClock(DateTimeOffset now) : IWarPollerClock
    {
        private readonly TaskCompletionSource _delayEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTimeOffset UtcNow => now;

        public int DelayCalls { get; private set; }
        public bool CancellationObserved { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCalls++;
            _delayEntered.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
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
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }

    private sealed class SingleServiceScopeFactory(IServiceProvider services) : IServiceScopeFactory
    {
        private readonly IServiceProvider _services = services;

        public IServiceScope CreateScope() => new SingleServiceScope(_services);
    }

    private sealed class SingleServiceScope(IServiceProvider services) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = services;

        public void Dispose()
        {
        }
    }
}
