using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using HappyGymStats.Api;
using HappyGymStats.Api.Hubs;
using HappyGymStats.Api.Models;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace HappyGymStats.Tests;

public sealed class WarApiHubEndpointTests : IClassFixture<SqliteApiEndpointTests.SqliteTestApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string ScopeKey = "public-war";
    private static readonly DateTimeOffset FixtureCapturedAtUtc = DateTimeOffset.FromUnixTimeSeconds(1731001800);
    private static readonly DateTimeOffset PriorSampleUtc = DateTimeOffset.FromUnixTimeSeconds(1731000900);

    private readonly SqliteApiEndpointTests.SqliteTestApplicationFactory _factory;

    public WarApiHubEndpointTests(SqliteApiEndpointTests.SqliteTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
        ResetWarTables();
    }

    [Fact]
    public async Task Current_endpoint_rejects_anonymous_requests()
    {
        using var anonymousApp = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = RejectingAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RejectingAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, RejectingAuthHandler>(RejectingAuthHandler.SchemeName, _ => { });
        }));
        using var client = anonymousApp.CreateClient();

        var response = await client.GetAsync("/api/v1/war/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Current_endpoint_returns_ok_and_not_ready_when_no_war_is_running()
    {
        // Nothing seeded: the constructor resets the war tables, so this is an
        // ordinary evening between wars.
        //
        // This used to answer 503 with code war_state_not_ready, which the board
        // rendered as "War board unavailable. The API service is currently
        // unavailable." Nothing was unavailable. A 503 here also tells
        // monitoring the service is down.
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString());

        var response = await client.GetAsync("/api/v1/war/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<WarStateDto>(JsonOptions);
        Assert.NotNull(state);
        Assert.Equal(WarStatus.NotReady, state.Status);
        Assert.False(state.IsReady);
        Assert.Null(state.WarId);
    }

    [Fact]
    public async Task Authenticated_current_and_health_return_seeded_state_and_stale_metadata()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var nowUtc = DateTimeOffset.UtcNow;
        await SeedCurrentWarAsync(report.War.WarId, FixtureCapturedAtUtc);
        await SeedRosterAsync(report, FixtureCapturedAtUtc);
        await SeedSamplesAsync(BuildFixtureSamples(report.War.WarId));
        await SeedHeartbeatAsync(new WarPollerHeartbeatEntity
        {
            ScopeKey = ScopeKey,
            Phase = "retryable-failure",
            UpdatedAtUtc = FixtureCapturedAtUtc,
            PollStartedAtUtc = FixtureCapturedAtUtc.AddSeconds(-30),
            PollCompletedAtUtc = FixtureCapturedAtUtc,
            RetryCount = 1,
            LastError = "timeout while polling war",
            ActiveWarId = report.War.WarId,
            StaleAfterUtc = nowUtc.AddSeconds(-1),
            PollIntervalSeconds = 30,
            FailureBackoffSeconds = 60,
        });

        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString());

        var currentResponse = await client.GetAsync("/api/v1/war/current");
        var currentError = await currentResponse.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, currentResponse.StatusCode);
        Assert.NotNull(currentError);
        Assert.Equal("war_state_not_ready", currentError.Error.Code);

        var state = JsonSerializer.Deserialize<WarStateDto>(JsonSerializer.Serialize(currentError.Error.Details), JsonOptions);
        Assert.NotNull(state);
        Assert.Equal("degraded", state.Status);
        Assert.Equal(report.War.WarId, state.WarId);
        Assert.True(state.HasRoster);
        Assert.Equal(2, state.FactionCount);
        Assert.Equal(5, state.MemberCount);
        Assert.Equal(0.75m, state.CoverageRatio);
        Assert.Equal("retryable-failure", state.Heartbeat.Phase);
        Assert.True(state.Heartbeat.IsStale);
        Assert.Equal("timeout while polling war", state.Heartbeat.LastError);
        Assert.Contains(state.Warnings, warning => warning.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(state.Holes, hole => hole.Severity == "high" || hole.Severity == "critical");

        var healthResponse = await client.GetAsync("/api/v1/war/health");
        healthResponse.EnsureSuccessStatusCode();
        var health = await healthResponse.Content.ReadFromJsonAsync<WarHealthDto>(JsonOptions);
        Assert.NotNull(health);
        Assert.Equal("degraded", health.Status);
        Assert.Equal(report.War.WarId, health.WarId);
        Assert.True(health.Heartbeat.IsStale);
        Assert.Equal(5, health.MemberCount);
        Assert.Equal(state.HoleCount, health.HoleCount);
    }

    [Fact]
    public async Task Internal_notify_broadcasts_same_current_state_dto_through_broadcaster_contract()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        await SeedCurrentWarAsync(report.War.WarId, FixtureCapturedAtUtc);
        await SeedRosterAsync(report, FixtureCapturedAtUtc);
        await SeedSamplesAsync(BuildFixtureSamples(report.War.WarId));
        await SeedHeartbeatAsync(new WarPollerHeartbeatEntity
        {
            ScopeKey = ScopeKey,
            Phase = "succeeded",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            PollStartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
            PollCompletedAtUtc = DateTimeOffset.UtcNow,
            RetryCount = 0,
            ActiveWarId = report.War.WarId,
            StaleAfterUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            PollIntervalSeconds = 30,
            FailureBackoffSeconds = 60,
        });

        var recorder = new WarHubBroadcastRecorder();
        using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWarHubBroadcaster>();
            services.AddSingleton(recorder);
            services.AddScoped<IWarHubBroadcaster, RecordingWarHubBroadcaster>();
        }));
        ResetWarTables(app.Services);
        await SeedCurrentWarAsync(app.Services, report.War.WarId, FixtureCapturedAtUtc);
        await SeedRosterAsync(app.Services, report, FixtureCapturedAtUtc);
        await SeedSamplesAsync(app.Services, BuildFixtureSamples(report.War.WarId));
        await SeedHeartbeatAsync(app.Services, new WarPollerHeartbeatEntity
        {
            ScopeKey = ScopeKey,
            Phase = "succeeded",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            PollStartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
            PollCompletedAtUtc = DateTimeOffset.UtcNow,
            RetryCount = 0,
            ActiveWarId = report.War.WarId,
            StaleAfterUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            PollIntervalSeconds = 30,
            FailureBackoffSeconds = 60,
        });

        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-AnonymousId", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Subject", "test-sub");
        var currentResponse = await client.GetFromJsonAsync<WarStateDto>("/api/v1/war/current", JsonOptions);
        Assert.NotNull(currentResponse);

        var notifyResponse = await client.PostAsync("/api/v1/war/internal/notify", content: null);

        Assert.Equal(HttpStatusCode.Accepted, notifyResponse.StatusCode);
        var payload = await notifyResponse.Content.ReadFromJsonAsync<WarNotifyAcceptedDto>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("broadcasted", payload.Status);
        Assert.NotNull(recorder.LastBroadcast);
        Assert.Equivalent(payload.State, recorder.LastBroadcast);
        Assert.Equal(currentResponse.WarId, payload.State.WarId);
        Assert.Equal(currentResponse.Status, payload.State.Status);
        Assert.Equal(currentResponse.FactionCount, payload.State.FactionCount);
        Assert.Equal(currentResponse.MemberCount, payload.State.MemberCount);
        Assert.Equal(currentResponse.CoverageRatio, payload.State.CoverageRatio);
        Assert.Equal(currentResponse.HoleCount, payload.State.HoleCount);
        Assert.Equal(currentResponse.Heartbeat.Phase, payload.State.Heartbeat.Phase);
        Assert.Equal(report.War.WarId, recorder.LastBroadcast!.WarId);
        Assert.Equal("ok", recorder.LastBroadcast.Status);
    }

    private void ResetWarTables()
        => ResetWarTables(_factory.Services);

    private static void ResetWarTables(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        db.WarRosterSnapshots.RemoveRange(db.WarRosterSnapshots);
        db.WarScoreSamples.RemoveRange(db.WarScoreSamples);
        db.WarCurrent.RemoveRange(db.WarCurrent);
        db.WarPollerHeartbeats.RemoveRange(db.WarPollerHeartbeats);
        db.SaveChanges();
    }

    private Task SeedCurrentWarAsync(long warId, DateTimeOffset observedAtUtc)
        => SeedCurrentWarAsync(_factory.Services, warId, observedAtUtc);

    private static async Task SeedCurrentWarAsync(IServiceProvider services, long warId, DateTimeOffset observedAtUtc)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        db.WarCurrent.Add(new WarCurrentEntity
        {
            ScopeKey = ScopeKey,
            WarId = warId,
            FactionId = 111,
            FactionName = "Happy Gym",
            OpponentFactionId = 222,
            OpponentFactionName = "Chain Breakers",
            StartedAtUtc = FixtureCapturedAtUtc.AddHours(-1),
            EndsAtUtc = null,
            IsLive = true,
            ObservedAtUtc = observedAtUtc,
        });
        await db.SaveChangesAsync();
    }

    private Task SeedRosterAsync(RankedWarReportResponse report, DateTimeOffset capturedAtUtc)
        => SeedRosterAsync(_factory.Services, report, capturedAtUtc);

    private static async Task SeedRosterAsync(IServiceProvider services, RankedWarReportResponse report, DateTimeOffset capturedAtUtc)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        db.WarRosterSnapshots.AddRange(report.Factions
            .SelectMany(faction => faction.Members.Select(member => new WarRosterSnapshotEntity
            {
                WarId = report.War.WarId,
                FactionId = faction.FactionId,
                FactionName = faction.Name,
                MemberId = member.UserId,
                MemberName = member.Name,
                Score = member.Score,
                Chain = member.Chain,
                Attacks = member.Attacks,
                StatusState = member.Status?.State,
                StatusUntilUtc = member.Status?.Until,
                CapturedAtUtc = capturedAtUtc,
            })));
        await db.SaveChangesAsync();
    }

    private Task SeedSamplesAsync(IEnumerable<WarScoreSampleEntity> samples)
        => SeedSamplesAsync(_factory.Services, samples);

    private static async Task SeedSamplesAsync(IServiceProvider services, IEnumerable<WarScoreSampleEntity> samples)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        db.WarScoreSamples.AddRange(samples);
        await db.SaveChangesAsync();
    }

    private Task SeedHeartbeatAsync(WarPollerHeartbeatEntity heartbeat)
        => SeedHeartbeatAsync(_factory.Services, heartbeat);

    private static async Task SeedHeartbeatAsync(IServiceProvider services, WarPollerHeartbeatEntity heartbeat)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        db.WarPollerHeartbeats.Add(heartbeat);
        await db.SaveChangesAsync();
    }

    private static T DeserializeFixture<T>(string relativePath)
    {
        var root = ResolveRepositoryRoot();
        var fullPath = Path.Combine(root, relativePath);
        var json = File.ReadAllText(fullPath);

        try
        {
            return JsonSerializer.Deserialize<T>(json, WarEndpointJson.SerializerOptions)
                ?? throw new XunitException($"Deserializer returned null for {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new XunitException($"Fixture '{relativePath}' failed to deserialize: {ex.Message}");
        }
    }

    private static IReadOnlyList<WarScoreSampleEntity> BuildFixtureSamples(long warId)
        =>
        [
            new WarScoreSampleEntity
            {
                Id = 1,
                WarId = warId,
                FactionId = 111,
                FactionName = "Happy Gym",
                FactionScore = 100,
                FactionChain = 30,
                OpponentFactionId = 222,
                OpponentFactionName = "Chain Breakers",
                OpponentScore = 90,
                OpponentChain = 27,
                SampledAtUtc = PriorSampleUtc,
            },
            new WarScoreSampleEntity
            {
                Id = 2,
                WarId = warId,
                FactionId = 111,
                FactionName = "Happy Gym",
                FactionScore = 128,
                FactionChain = 42,
                OpponentFactionId = 222,
                OpponentFactionName = "Chain Breakers",
                OpponentScore = 117,
                OpponentChain = 39,
                SampledAtUtc = FixtureCapturedAtUtc,
            },
        ];

    private static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
    }

    private sealed class WarHubBroadcastRecorder
    {
        public WarStateDto? LastBroadcast { get; set; }
    }

    private sealed class RejectingAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RejectAll";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Fail("anonymous"));
    }

    private sealed class RecordingWarHubBroadcaster(
        WarDerivedStateService warDerivedStateService,
        WarHubBroadcastRecorder recorder) : IWarHubBroadcaster
    {
        public async Task<WarStateDto> BroadcastCurrentStateAsync(CancellationToken ct)
        {
            recorder.LastBroadcast = (await warDerivedStateService.GetCurrentAsync(ScopeKey, ct: ct)).ToStateDto();
            return recorder.LastBroadcast;
        }
    }
}
