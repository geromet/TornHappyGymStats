using System.Text.Json;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace HappyGymStats.Tests;

public sealed class WarDerivedStateServiceTests
{
    private const string ScopeKey = "public-war";
    private static readonly DateTimeOffset FixtureCapturedAtUtc = DateTimeOffset.FromUnixTimeSeconds(1731001800);
    private static readonly DateTimeOffset PriorSampleUtc = DateTimeOffset.FromUnixTimeSeconds(1731000900);

    [Fact]
    public async Task GetCurrentAsync_composes_fixture_backed_current_state_from_persisted_rows()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var capturedAtUtc = FixtureCapturedAtUtc;
        var nowUtc = capturedAtUtc.AddMinutes(5);

        await SeedCurrentWarAsync(persistence, report.War.WarId, capturedAtUtc);
        await SeedRosterAsync(persistence, report, capturedAtUtc);
        await SeedSamplesAsync(persistence, BuildFixtureSamples(report.War.WarId));
        await SeedHeartbeatAsync(
            persistence,
            new WarPollerHeartbeatEntity
            {
                ScopeKey = ScopeKey,
                Phase = "succeeded",
                UpdatedAtUtc = capturedAtUtc,
                PollStartedAtUtc = capturedAtUtc.AddSeconds(-30),
                PollCompletedAtUtc = capturedAtUtc,
                RetryCount = 0,
                ActiveWarId = report.War.WarId,
                StaleAfterUtc = nowUtc.AddMinutes(1),
                PollIntervalSeconds = 30,
                FailureBackoffSeconds = 60,
            });

        var sut = new WarDerivedStateService(persistence.WarRepository, new FrozenTimeProvider(nowUtc));

        var state = await sut.GetCurrentAsync(ScopeKey, report.IdleAttackers, CancellationToken.None);

        Assert.Equal(48377, state.WarId);
        Assert.Equal(nowUtc, state.AsOfUtc);
        Assert.Equal(capturedAtUtc, state.RosterCapturedAtUtc);
        Assert.Equal(PriorSampleUtc, state.ScoreWindowStartedAtUtc);
        Assert.Equal(capturedAtUtc, state.ScoreWindowEndedAtUtc);
        Assert.Equal(2, state.ScoreSampleCount);
        Assert.Equal("succeeded", state.HeartbeatPhase);
        Assert.Equal(capturedAtUtc, state.HeartbeatUpdatedAtUtc);
        Assert.Equal(capturedAtUtc.AddSeconds(-30), state.HeartbeatPollStartedAtUtc);
        Assert.Equal(capturedAtUtc, state.HeartbeatPollCompletedAtUtc);
        Assert.Equal(nowUtc.AddMinutes(1), state.HeartbeatStaleAfterUtc);
        Assert.False(state.IsHeartbeatStale);
        Assert.Null(state.HeartbeatLastError);
        Assert.Equal(0.5m, state.CoverageRatio);
        Assert.Empty(state.Errors);
        Assert.DoesNotContain(state.Warnings, warning => warning.Contains("No current war", StringComparison.Ordinal));

        var home = Assert.Single(state.Factions.Where(faction => faction.FactionId == 111));
        Assert.Equal(2, home.AvailableMemberCount);
        Assert.Equal(1, home.HospitalizedMemberCount);
        Assert.True(home.ScoreRate.IsAvailable);
        Assert.True(home.Eta.IsAvailable);
        Assert.True(home.AttacksToFinish.IsAvailable);
        Assert.Equal(1500, Assert.Single(home.Members.Where(member => member.MemberId == 1001)).HospitalCountdownSeconds);

        Assert.Contains(state.Holes, hole => hole.Kind == WarHoleKind.IdleAttacker && hole.FactionId == 111 && hole.MemberId == 1003);
        Assert.Contains(state.Holes, hole => hole.Kind == WarHoleKind.OpenTarget && hole.FactionId == 222 && hole.MemberId == 1002);
    }

    [Fact]
    public async Task GetCurrentAsync_without_current_war_returns_not_ready_state_with_heartbeat_metadata()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var nowUtc = FixtureCapturedAtUtc.AddMinutes(5);
        await SeedHeartbeatAsync(
            persistence,
            new WarPollerHeartbeatEntity
            {
                ScopeKey = ScopeKey,
                Phase = "idle",
                UpdatedAtUtc = FixtureCapturedAtUtc,
                StaleAfterUtc = nowUtc.AddMinutes(1),
                PollIntervalSeconds = 30,
                FailureBackoffSeconds = 60,
            });

        var sut = new WarDerivedStateService(persistence.WarRepository, new FrozenTimeProvider(nowUtc));

        var state = await sut.GetCurrentAsync(ScopeKey, ct: CancellationToken.None);

        Assert.Null(state.WarId);
        Assert.Empty(state.Factions);
        Assert.Empty(state.Holes);
        Assert.Equal("idle", state.HeartbeatPhase);
        Assert.False(state.IsHeartbeatStale);
        Assert.Contains("No current war is available for the requested scope.", state.Warnings);
    }

    [Fact]
    public async Task GetCurrentAsync_with_empty_roster_returns_diagnostic_empty_state()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var nowUtc = FixtureCapturedAtUtc.AddMinutes(5);

        await SeedCurrentWarAsync(persistence, report.War.WarId, FixtureCapturedAtUtc);
        await SeedSamplesAsync(persistence, BuildFixtureSamples(report.War.WarId));
        await SeedHeartbeatAsync(persistence, BuildHeartbeat("succeeded", nowUtc.AddMinutes(1), report.War.WarId));

        var sut = new WarDerivedStateService(persistence.WarRepository, new FrozenTimeProvider(nowUtc));

        var state = await sut.GetCurrentAsync(ScopeKey, ct: CancellationToken.None);

        Assert.Equal(report.War.WarId, state.WarId);
        Assert.Empty(state.Factions);
        Assert.Empty(state.Holes);
        Assert.Contains("No roster snapshot rows were provided.", state.Warnings);
        Assert.Equal(2, state.ScoreSampleCount);
    }

    [Fact]
    public async Task GetCurrentAsync_with_single_score_sample_surfaces_insufficient_rate_data()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var nowUtc = FixtureCapturedAtUtc.AddMinutes(5);

        await SeedCurrentWarAsync(persistence, report.War.WarId, FixtureCapturedAtUtc);
        await SeedRosterAsync(persistence, report, FixtureCapturedAtUtc);
        await SeedSamplesAsync(persistence, BuildFixtureSamples(report.War.WarId).Take(1).ToArray());
        await SeedHeartbeatAsync(persistence, BuildHeartbeat("succeeded", nowUtc.AddMinutes(1), report.War.WarId));

        var sut = new WarDerivedStateService(persistence.WarRepository, new FrozenTimeProvider(nowUtc));

        var state = await sut.GetCurrentAsync(ScopeKey, report.IdleAttackers, CancellationToken.None);

        Assert.All(state.Factions, faction =>
        {
            Assert.False(faction.ScoreRate.IsAvailable);
            Assert.Equal("insufficient-score-samples", faction.ScoreRate.Diagnostic);
            Assert.False(faction.Eta.IsAvailable);
        });
        Assert.Contains(state.Warnings, warning => warning.Contains("does not have enough score samples", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCurrentAsync_with_stale_heartbeat_sets_stale_metadata_and_warning()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var nowUtc = FixtureCapturedAtUtc.AddMinutes(5);

        await SeedCurrentWarAsync(persistence, report.War.WarId, FixtureCapturedAtUtc);
        await SeedRosterAsync(persistence, report, FixtureCapturedAtUtc);
        await SeedSamplesAsync(persistence, BuildFixtureSamples(report.War.WarId));
        await SeedHeartbeatAsync(
            persistence,
            new WarPollerHeartbeatEntity
            {
                ScopeKey = ScopeKey,
                Phase = "retryable-failure",
                UpdatedAtUtc = FixtureCapturedAtUtc,
                StaleAfterUtc = nowUtc.AddSeconds(-1),
                LastError = "TornApiException: retryable",
                ActiveWarId = report.War.WarId,
                PollIntervalSeconds = 30,
                FailureBackoffSeconds = 60,
            });

        var sut = new WarDerivedStateService(persistence.WarRepository, new FrozenTimeProvider(nowUtc));

        var state = await sut.GetCurrentAsync(ScopeKey, report.IdleAttackers, CancellationToken.None);

        Assert.True(state.IsHeartbeatStale);
        Assert.Equal("retryable-failure", state.HeartbeatPhase);
        Assert.Equal("TornApiException: retryable", state.HeartbeatLastError);
        Assert.Contains(state.Warnings, warning => warning.Contains("stale", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCurrentAsync_honors_cancelled_request()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = new WarDerivedStateService(persistence.WarRepository, new FrozenTimeProvider(FixtureCapturedAtUtc));

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetCurrentAsync(ScopeKey, ct: cts.Token));
    }

    [Fact]
    public async Task GetCurrentAsync_warns_for_out_of_roster_idle_attacker_ids()
    {
        await using var persistence = await TestPersistenceScope.CreateAsync();
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var nowUtc = FixtureCapturedAtUtc.AddMinutes(5);

        await SeedCurrentWarAsync(persistence, report.War.WarId, FixtureCapturedAtUtc);
        await SeedRosterAsync(persistence, report, FixtureCapturedAtUtc);
        await SeedSamplesAsync(persistence, BuildFixtureSamples(report.War.WarId));
        await SeedHeartbeatAsync(persistence, BuildHeartbeat("succeeded", nowUtc.AddMinutes(1), report.War.WarId));

        var sut = new WarDerivedStateService(persistence.WarRepository, new FrozenTimeProvider(nowUtc));

        var state = await sut.GetCurrentAsync(ScopeKey, [9999], CancellationToken.None);

        Assert.Contains(state.Warnings, warning => warning.Contains("Idle attacker id 9999", StringComparison.Ordinal));
    }

    private static async Task SeedCurrentWarAsync(TestPersistenceScope persistence, long warId, DateTimeOffset observedAtUtc)
    {
        await persistence.WarRepository.UpsertCurrentAsync(
            new WarCurrentEntity
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
            },
            CancellationToken.None);
        await persistence.DbContext.SaveChangesAsync();
    }

    private static async Task SeedRosterAsync(TestPersistenceScope persistence, RankedWarReportResponse report, DateTimeOffset capturedAtUtc)
    {
        await persistence.WarRepository.ReplaceRosterSnapshotAsync(report.War.WarId, MapRoster(report, capturedAtUtc), CancellationToken.None);
        await persistence.DbContext.SaveChangesAsync();
    }

    private static async Task SeedSamplesAsync(TestPersistenceScope persistence, IReadOnlyCollection<WarScoreSampleEntity> samples)
    {
        foreach (var sample in samples)
        {
            await persistence.WarRepository.AddScoreSampleAsync(sample, CancellationToken.None);
        }

        await persistence.DbContext.SaveChangesAsync();
    }

    private static async Task SeedHeartbeatAsync(TestPersistenceScope persistence, WarPollerHeartbeatEntity heartbeat)
    {
        await persistence.WarRepository.UpsertHeartbeatAsync(heartbeat, CancellationToken.None);
        await persistence.DbContext.SaveChangesAsync();
    }

    private static WarPollerHeartbeatEntity BuildHeartbeat(string phase, DateTimeOffset staleAfterUtc, long warId)
        => new()
        {
            ScopeKey = ScopeKey,
            Phase = phase,
            UpdatedAtUtc = FixtureCapturedAtUtc,
            PollStartedAtUtc = FixtureCapturedAtUtc.AddSeconds(-30),
            PollCompletedAtUtc = FixtureCapturedAtUtc,
            RetryCount = 0,
            ActiveWarId = warId,
            StaleAfterUtc = staleAfterUtc,
            PollIntervalSeconds = 30,
            FailureBackoffSeconds = 60,
        };

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

    private static WarRosterSnapshotEntity[] MapRoster(RankedWarReportResponse report, DateTimeOffset capturedAtUtc)
        => report.Factions
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
            }))
            .ToArray();

    private static WarScoreSampleEntity[] BuildFixtureSamples(long warId)
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
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestPersistenceScope : IAsyncDisposable
    {
        private TestPersistenceScope(SqliteConnection connection, HappyGymStatsDbContext dbContext)
        {
            Connection = connection;
            DbContext = dbContext;
            WarRepository = new WarStateRepository(dbContext);
        }

        public SqliteConnection Connection { get; }
        public HappyGymStatsDbContext DbContext { get; }
        public IWarStateRepository WarRepository { get; }

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
