using System.Net;
using System.Text;
using System.Text.Json;
using HappyGymStats.Api;
using HappyGymStats.Core.Fetch;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Storage;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Storage;
using HappyGymStats.Legacy.Cli.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class DbPipelineIntegrationTests
{
    [Fact]
    public async Task Fetch_writes_raw_logs_checkpoint_and_run_state_into_sqlite()
    {
        var tempDir = CreateTempDirectory();
        var paths = new AppPaths(
            DataDirectory: tempDir,
            QuarantineDirectory: Path.Combine(tempDir, "quarantine"),
            CheckpointPath: Path.Combine(tempDir, "checkpoint.json"),
            LogsJsonlPath: Path.Combine(tempDir, "userlogs.jsonl"));

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "log": [
                    {
                      "id": "log-1",
                      "timestamp": 1777546189,
                      "details": { "title": "Gym train", "category": "Gym" },
                      "data": { "happy_used": 25 }
                    }
                  ],
                  "_metadata": { "links": { "prev": null } }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        var fetcher = new LogFetcher(paths, new TornApiClient(httpClient));
        var result = await fetcher.RunAsync(
            apiKey: "test-key",
            mode: FetchMode.Fresh,
            options: FetchOptions.Default(new Uri("https://example.test/user/log?cat=25"), TimeSpan.Zero),
            ct: CancellationToken.None);

        Assert.Equal(1, result.PagesFetched);
        Assert.Equal(1, result.LogsFetched);
        Assert.Equal(1, result.LogsAppended);
        Assert.True(File.Exists(paths.LogsJsonlPath));
        Assert.True(File.Exists(paths.CheckpointPath));

        var dbPath = SqlitePaths.ResolveDatabasePath(paths.DataDirectory);
        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var db = new HappyGymStatsDbContext(dbOptions);
        var rawRows = await db.RawUserLogs.AsNoTracking().ToListAsync();
        Assert.Single(rawRows);
        Assert.Equal("log-1", rawRows[0].LogId);

        var checkpoint = await db.ImportCheckpoints.AsNoTracking().SingleAsync();
        Assert.Equal("completed", checkpoint.LastRunOutcome);
        Assert.Equal(1, checkpoint.TotalFetchedCount);
        Assert.Equal(1, checkpoint.TotalAppendedCount);

        var run = await db.ImportRuns.AsNoTracking().SingleAsync();
        Assert.Equal("completed", run.Outcome);
        Assert.Equal(1, run.PagesFetched);
        Assert.Equal(1, run.LogsFetched);
        Assert.Equal(1, run.LogsAppended);
    }

    [Fact]
    public async Task Reconstruction_can_read_from_sqlite_when_legacy_jsonl_is_missing()
    {
        var fixture = DatasetPaths.Discover();
        using var _ = fixture.UseRepoRootAsCurrentDirectory();

        var tempDir = CreateTempDirectory();
        var tempInputPaths = new AppPaths(
            DataDirectory: tempDir,
            QuarantineDirectory: Path.Combine(tempDir, "quarantine"),
            CheckpointPath: Path.Combine(Path.GetDirectoryName(fixture.UserLogsJsonlPath)!, "checkpoint.json"),
            LogsJsonlPath: fixture.UserLogsJsonlPath);

        var dbPath = SqlitePaths.ResolveDatabasePath(tempDir);
        var migrate = await LegacySqliteMigrator.RunAsync(tempInputPaths, dbPath, CancellationToken.None);
        Assert.True(migrate.Success, migrate.ErrorMessage);

        var runtimePaths = new AppPaths(
            DataDirectory: tempDir,
            QuarantineDirectory: Path.Combine(tempDir, "quarantine"),
            CheckpointPath: Path.Combine(tempDir, "checkpoint.json"),
            LogsJsonlPath: Path.Combine(tempDir, "missing-userlogs.jsonl"));

        var runner = new ReconstructionRunner(runtimePaths);
        var result = runner.Run(
            currentHappy: 0,
            anchorTimeUtc: DateTimeOffset.UtcNow,
            ct: CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotEmpty(result.DerivedGymTrains);
        Assert.True(File.Exists(runtimePaths.DerivedGymTrainsJsonlPath));
        Assert.True(File.Exists(runtimePaths.DerivedHappyEventsJsonlPath));

        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var db = new HappyGymStatsDbContext(dbOptions);
        var derivedTrainsCount = await db.DerivedGymTrains.AsNoTracking().CountAsync();
        Assert.True(derivedTrainsCount > 0);
        Assert.True(await db.DerivedHappyEvents.AsNoTracking().AnyAsync());

        var provenance = await db.ModifierProvenance
            .AsNoTracking()
            .OrderBy(p => p.DerivedGymTrainLogId)
            .ThenBy(p => p.Scope)
            .ToListAsync();

        Assert.Equal(derivedTrainsCount * 3, provenance.Count);

        Assert.All(
            provenance.Where(p => p.Scope == HappyReconstructionModels.ModifierProvenanceScopes.Personal),
            row =>
            {
                Assert.Equal(HappyReconstructionModels.ModifierProvenanceStatuses.Verified, row.VerificationStatus);
                Assert.Equal(HappyReconstructionModels.ModifierProvenanceReasonCodes.SourceLog, row.VerificationReasonCode);
                Assert.False(string.IsNullOrWhiteSpace(row.SubjectId));
            });

        Assert.All(
            provenance.Where(p => p.Scope == HappyReconstructionModels.ModifierProvenanceScopes.Faction),
            row =>
            {
                Assert.Equal(HappyReconstructionModels.ModifierProvenanceStatuses.Unresolved, row.VerificationStatus);
                Assert.Equal(HappyReconstructionModels.ModifierProvenanceReasonCodes.MissingFactionRecord, row.VerificationReasonCode);
                Assert.False(string.IsNullOrWhiteSpace(row.FactionId));
            });

        Assert.All(
            provenance.Where(p => p.Scope == HappyReconstructionModels.ModifierProvenanceScopes.Company),
            row =>
            {
                Assert.Equal(HappyReconstructionModels.ModifierProvenanceStatuses.Unresolved, row.VerificationStatus);
                Assert.Equal(HappyReconstructionModels.ModifierProvenanceReasonCodes.MissingCompanyRecord, row.VerificationReasonCode);
                Assert.False(string.IsNullOrWhiteSpace(row.CompanyId));
            });
    }

    [Fact]
    public async Task Surfaces_cache_payload_includes_confidence_and_stable_reason_codes_for_verified_and_unresolved_rows()
    {
        var tempDir = CreateTempDirectory();
        var dbPath = SqlitePaths.ResolveDatabasePath(tempDir);

        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var db = new HappyGymStatsDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();

            db.RawUserLogs.AddRange(
                new RawUserLogEntity
                {
                    LogId = "log-verified",
                    OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    RawJson = """{"details":{"category":"Gym"},"data":{"energy_used":10,"strength_before":1000,"strength_increased":12.5}}"""
                },
                new RawUserLogEntity
                {
                    LogId = "log-unresolved",
                    OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:05:00Z"),
                    RawJson = """{"details":{"category":"Gym"},"data":{"energy_used":10,"speed_before":900,"speed_increased":8.0}}"""
                });

            db.DerivedGymTrains.AddRange(
                new DerivedGymTrainEntity { LogId = "log-verified", OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"), HappyBeforeTrain = 4200 },
                new DerivedGymTrainEntity { LogId = "log-unresolved", OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:05:00Z"), HappyBeforeTrain = 3900 });

            db.ModifierProvenance.AddRange(
                new ModifierProvenanceEntity
                {
                    DerivedGymTrainLogId = "log-verified",
                    Scope = "personal",
                    SubjectId = "user-1",
                    VerificationStatus = "verified",
                    VerificationReasonCode = "source-log",
                    ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
                },
                new ModifierProvenanceEntity
                {
                    DerivedGymTrainLogId = "log-unresolved",
                    Scope = "faction",
                    FactionId = "unknown-faction",
                    VerificationStatus = "unresolved",
                    VerificationReasonCode = "missing-faction-record",
                    ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
                });

            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        await using var provider = services.BuildServiceProvider();

        var cacheDir = Path.Combine(tempDir, "surfaces");
        var writer = new SurfacesCacheWriter(provider.GetRequiredService<IServiceScopeFactory>(), cacheDir);
        await writer.WriteLatestAsync("test-v1", DateTimeOffset.Parse("2026-01-01T01:00:00Z"), CancellationToken.None);

        var latestPath = Path.Combine(cacheDir, "latest.json");
        Assert.True(File.Exists(latestPath));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(latestPath));
        var gymCloud = json.RootElement.GetProperty("series").GetProperty("gymCloud");

        var confidence = gymCloud.GetProperty("confidence").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.Equal(new[] { 1.0, 0.75 }, confidence);

        var reasons = gymCloud.GetProperty("confidenceReasons")
            .EnumerateArray()
            .Select(arr => arr.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToArray())
            .ToArray();

        Assert.Equal(new[] { "source-log" }, reasons[0]);
        Assert.Equal(new[] { "missing-faction-record" }, reasons[1]);

        var warnings = gymCloud.GetProperty("provenanceWarnings").EnumerateArray().ToArray();
        Assert.Single(warnings);
        Assert.Equal("log-unresolved", warnings[0].GetProperty("LogId").GetString());
        Assert.Equal("faction", warnings[0].GetProperty("Scope").GetString());
        Assert.Equal("unresolved", warnings[0].GetProperty("VerificationStatus").GetString());
        Assert.Equal("missing-faction-record", warnings[0].GetProperty("ReasonCode").GetString());
        Assert.Equal("/factions/unknown-faction", warnings[0].GetProperty("LinkTarget").GetString());
        Assert.Equal(1, warnings[0].GetProperty("RowCount").GetInt32());

        var warningDiagnostics = json.RootElement.GetProperty("meta").GetProperty("provenanceWarningsDiagnostics");
        Assert.Equal(1, warningDiagnostics.GetProperty("warningCount").GetInt32());
        Assert.Equal(0, warningDiagnostics.GetProperty("skippedMalformedRowCount").GetInt32());
        Assert.False(warningDiagnostics.GetProperty("queryFailed").GetBoolean());

        Assert.True(gymCloud.TryGetProperty("x", out _));
        Assert.True(gymCloud.TryGetProperty("y", out _));
        Assert.True(gymCloud.TryGetProperty("z", out _));
        Assert.True(gymCloud.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Surfaces_cache_payload_uses_missing_provenance_fallback_reason_when_join_rows_are_absent()
    {
        var tempDir = CreateTempDirectory();
        var dbPath = SqlitePaths.ResolveDatabasePath(tempDir);

        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var db = new HappyGymStatsDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();

            db.RawUserLogs.Add(new RawUserLogEntity
            {
                LogId = "log-no-provenance",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                RawJson = """{"details":{"category":"Gym"},"data":{"energy_used":5,"dexterity_before":500,"dexterity_increased":5}}"""
            });

            db.DerivedGymTrains.Add(new DerivedGymTrainEntity
            {
                LogId = "log-no-provenance",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                HappyBeforeTrain = 2500
            });

            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        await using var provider = services.BuildServiceProvider();

        var cacheDir = Path.Combine(tempDir, "surfaces");
        var writer = new SurfacesCacheWriter(provider.GetRequiredService<IServiceScopeFactory>(), cacheDir);
        await writer.WriteLatestAsync("test-v1", DateTimeOffset.UtcNow, CancellationToken.None);

        var latestPath = Path.Combine(cacheDir, "latest.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(latestPath));
        var gymCloud = json.RootElement.GetProperty("series").GetProperty("gymCloud");

        var confidence = gymCloud.GetProperty("confidence").EnumerateArray().Select(x => x.GetDouble()).Single();
        Assert.Equal(0.2, confidence);

        var reasons = gymCloud.GetProperty("confidenceReasons")
            .EnumerateArray()
            .Single()
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => x is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(new[] { "missing-provenance-record" }, reasons);
        Assert.Empty(gymCloud.GetProperty("provenanceWarnings").EnumerateArray());

        var warningDiagnostics = json.RootElement.GetProperty("meta").GetProperty("provenanceWarningsDiagnostics");
        Assert.Equal(0, warningDiagnostics.GetProperty("warningCount").GetInt32());
        Assert.Equal(0, warningDiagnostics.GetProperty("skippedMalformedRowCount").GetInt32());
    }

    [Fact]
    public async Task Surfaces_cache_payload_skips_malformed_provenance_rows_and_keeps_warning_order_deterministic()
    {
        var tempDir = CreateTempDirectory();
        var dbPath = SqlitePaths.ResolveDatabasePath(tempDir);

        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var db = new HappyGymStatsDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();

            db.RawUserLogs.Add(new RawUserLogEntity
            {
                LogId = "log-a",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                RawJson = """{"details":{"category":"Gym"},"data":{"energy_used":5,"strength_before":500,"strength_increased":5}}"""
            });

            db.DerivedGymTrains.Add(new DerivedGymTrainEntity
            {
                LogId = "log-a",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                HappyBeforeTrain = 2500
            });

            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ModifierProvenance (DerivedGymTrainLogId, Scope, SubjectId, FactionId, CompanyId, ValidFromUtc, VerificationStatus, VerificationReasonCode) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7});",
                "log-a", "faction", null, "f1", null, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "unresolved", "missing-faction-record");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ModifierProvenance (DerivedGymTrainLogId, Scope, SubjectId, FactionId, CompanyId, ValidFromUtc, VerificationStatus, VerificationReasonCode) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7});",
                "log-a", "invalid-scope", null, null, null, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "unresolved", "bad-scope");
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF;");
        }

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        await using var provider = services.BuildServiceProvider();

        var cacheDir = Path.Combine(tempDir, "surfaces");
        var writer = new SurfacesCacheWriter(provider.GetRequiredService<IServiceScopeFactory>(), cacheDir);
        await writer.WriteLatestAsync("test-v1", DateTimeOffset.UtcNow, CancellationToken.None);

        var latestPath = Path.Combine(cacheDir, "latest.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(latestPath));

        var warnings = json.RootElement.GetProperty("series").GetProperty("gymCloud").GetProperty("provenanceWarnings")
            .EnumerateArray()
            .Select(w => new
            {
                LogId = w.GetProperty("LogId").GetString(),
                Scope = w.GetProperty("Scope").GetString(),
                Status = w.GetProperty("VerificationStatus").GetString(),
                Reason = w.GetProperty("ReasonCode").GetString()
            })
            .ToArray();

        Assert.Single(warnings);
        Assert.Equal("log-a", warnings[0].LogId);
        Assert.Equal("faction", warnings[0].Scope);
        Assert.Equal("unresolved", warnings[0].Status);
        Assert.Equal("missing-faction-record", warnings[0].Reason);

        var warningDiagnostics = json.RootElement.GetProperty("meta").GetProperty("provenanceWarningsDiagnostics");
        Assert.Equal(1, warningDiagnostics.GetProperty("warningCount").GetInt32());
        Assert.Equal(1, warningDiagnostics.GetProperty("skippedMalformedRowCount").GetInt32());
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "happygymstats-db-pipeline-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed record DatasetPaths(string RepoRoot, string UserLogsJsonlPath)
    {
        public static DatasetPaths Discover()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
                dir = dir.Parent;

            if (dir is null)
                throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");

            var dataRoot = Path.Combine(dir.FullName, "src", "HappyGymStats.Cli", "bin", "Debug", "net8.0", "data");
            return new DatasetPaths(
                RepoRoot: dir.FullName,
                UserLogsJsonlPath: Path.Combine(dataRoot, "userlogs.jsonl"));
        }

        public IDisposable UseRepoRootAsCurrentDirectory() => new CurrentDirectoryScope(RepoRoot);
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _originalDirectory;

        public CurrentDirectoryScope(string newDirectory)
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(newDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
        }
    }
}
