using System.Net;
using System.Text;
using System.Text.Json;
using HappyGymStats.Core.Fetch;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Surfaces;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using HappyGymStats.Data.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class DbPipelineIntegrationTests
{
    [Fact]
    public async Task Fetch_writes_user_log_entries_and_run_state_into_sqlite()
    {
        var tempDir = CreateTempDirectory();
        var dbPath = SqlitePaths.ResolveDatabasePath(tempDir);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "log": [
                    {
                      "id": "log-1",
                      "timestamp": 1777546189,
                      "details": { "id": 5301, "title": "Gym train defense", "category": "Gym" },
                      "data": { "happy_used": 25, "energy_used": 20, "defense_before": "7566.34", "defense_increased": "30.6" }
                    }
                  ],
                  "_metadata": { "links": { "prev": null } }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
        services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
        services.AddScoped<IImportRunRepository, ImportRunRepository>();
        services.AddScoped<LogFetcher>();
        services.AddSingleton(new TornApiClient(httpClient));
        await using var provider = services.BuildServiceProvider();

        using (var migrationScope = provider.CreateScope())
        {
            var migrationDb = migrationScope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
            await migrationDb.Database.MigrateAsync();
        }

        using var scope = provider.CreateScope();
        var fetcher = scope.ServiceProvider.GetRequiredService<LogFetcher>();
        var result = await fetcher.RunAsync(
            apiKey: "test-key",
            playerId: 42,
            mode: FetchMode.Fresh,
            options: FetchOptions.Default(new Uri("https://example.test/user/log?cat=25"), TimeSpan.Zero),
            ct: CancellationToken.None);

        Assert.Equal(1, result.PagesFetched);
        Assert.Equal(1, result.LogsFetched);
        Assert.Equal(1, result.LogsAppended);

        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var db = new HappyGymStatsDbContext(dbOptions);
        var entries = await db.UserLogEntries.AsNoTracking().ToListAsync();
        Assert.Single(entries);
        Assert.Equal("log-1", entries[0].LogEntryId);
        Assert.Equal(42, entries[0].PlayerId);
        Assert.Equal(5301, entries[0].LogTypeId);
        Assert.Equal(25, entries[0].HappyUsed);

        var run = await db.ImportRuns.AsNoTracking().SingleAsync();
        Assert.Equal("completed", run.Outcome);
        Assert.Equal(42, run.PlayerId);
        Assert.Equal(1, run.PagesFetched);
        Assert.Equal(1, run.LogsFetched);
        Assert.Equal(1, run.LogsAppended);
        Assert.Null(run.NextUrl);
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

            db.UserLogEntries.AddRange(
                new UserLogEntryEntity
                {
                    PlayerId = 0,
                    LogEntryId = "log-verified",
                    OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    LogTypeId = 1,
                    HappyUsed = 25,
                    HappyBeforeTrain = 4200,
                    EnergyUsed = 10,
                    StrengthBefore = 1000,
                    StrengthIncreased = 12.5,
                },
                new UserLogEntryEntity
                {
                    PlayerId = 0,
                    LogEntryId = "log-unresolved",
                    OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:05:00Z"),
                    LogTypeId = 2,
                    HappyUsed = 25,
                    HappyBeforeTrain = 3900,
                    EnergyUsed = 10,
                    SpeedBefore = 900,
                    SpeedIncreased = 8.0,
                });

            db.ModifierProvenance.AddRange(
                new ModifierProvenanceEntity
                {
                    PlayerId = 0,
                    LogEntryId = "log-verified",
                    Scope = (int)ModifierScope.Personal,
                    SubjectId = 1,
                    VerificationStatus = (int)VerificationStatus.Verified,
                },
                new ModifierProvenanceEntity
                {
                    PlayerId = 0,
                    LogEntryId = "log-unresolved",
                    Scope = (int)ModifierScope.Faction,
                    FactionId = 9001,
                    VerificationStatus = (int)VerificationStatus.Unresolved,
                });

            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
        services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
        services.AddScoped<IModifierProvenanceRepository, ModifierProvenanceRepository>();
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

        Assert.Equal(new[] { "personal-verified" }, reasons[0]);
        Assert.Equal(new[] { "faction-unresolved" }, reasons[1]);

        var warnings = gymCloud.GetProperty("provenanceWarnings").EnumerateArray().ToArray();
        Assert.Single(warnings);
        Assert.Equal("log-unresolved", warnings[0].GetProperty("LogId").GetString());
        Assert.Equal("faction", warnings[0].GetProperty("Scope").GetString());
        Assert.Equal("unresolved", warnings[0].GetProperty("VerificationStatus").GetString());
        Assert.Equal("/factions/9001", warnings[0].GetProperty("LinkTarget").GetString());
        Assert.False(warnings[0].GetProperty("HasManualOverride").GetBoolean());
        Assert.Equal(JsonValueKind.Null, warnings[0].GetProperty("ManualOverrideSource").ValueKind);
        Assert.Equal(1, warnings[0].GetProperty("RowCount").GetInt32());

        var warningDiagnostics = json.RootElement.GetProperty("meta").GetProperty("provenanceWarningsDiagnostics");
        Assert.Equal(1, warningDiagnostics.GetProperty("warningCount").GetInt32());
        Assert.Equal(0, warningDiagnostics.GetProperty("skippedMalformedRowCount").GetInt32());
        Assert.Equal(0, warningDiagnostics.GetProperty("overrideLoadedEntryCount").GetInt32());
        Assert.Equal(0, warningDiagnostics.GetProperty("overrideSkippedMalformedEntryCount").GetInt32());
        Assert.False(warningDiagnostics.GetProperty("overrideHitEntryCap").GetBoolean());
        Assert.Contains("override-read-failed", warningDiagnostics.GetProperty("overrideDiagnostics").EnumerateArray().Select(x => x.GetString()));
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

            db.UserLogEntries.Add(new UserLogEntryEntity
            {
                PlayerId = 0,
                LogEntryId = "log-no-provenance",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                LogTypeId = 1,
                HappyUsed = 10,
                HappyBeforeTrain = 2500,
                EnergyUsed = 5,
                DexterityBefore = 500,
                DexterityIncreased = 5,
            });

            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
        services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
        services.AddScoped<IModifierProvenanceRepository, ModifierProvenanceRepository>();
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

            db.UserLogEntries.Add(new UserLogEntryEntity
            {
                PlayerId = 0,
                LogEntryId = "log-a",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                LogTypeId = 1,
                HappyUsed = 10,
                HappyBeforeTrain = 2500,
                EnergyUsed = 5,
                StrengthBefore = 500,
                StrengthIncreased = 5,
            });

            await db.SaveChangesAsync();

            // Insert one valid faction/unresolved row and one row with an out-of-range scope (99)
            // that ScopeIntToString maps to "scope-99", which is not in KnownScopes and gets skipped.
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ModifierProvenance (PlayerId, LogEntryId, Scope, FactionId, VerificationStatus) VALUES (0, 'log-a', 2, 9001, 2);");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ModifierProvenance (PlayerId, LogEntryId, Scope, VerificationStatus) VALUES (0, 'log-a', 99, 2);");
        }

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());
        services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
        services.AddScoped<IModifierProvenanceRepository, ModifierProvenanceRepository>();
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
            })
            .ToArray();

        Assert.Single(warnings);
        Assert.Equal("log-a", warnings[0].LogId);
        Assert.Equal("faction", warnings[0].Scope);
        Assert.Equal("unresolved", warnings[0].Status);

        var warningDiagnostics = json.RootElement.GetProperty("meta").GetProperty("provenanceWarningsDiagnostics");
        Assert.Equal(1, warningDiagnostics.GetProperty("warningCount").GetInt32());
        Assert.Equal(1, warningDiagnostics.GetProperty("skippedMalformedRowCount").GetInt32());
    }

    [Fact]
    public async Task Surfaces_cache_payload_applies_local_manual_override_for_unresolved_faction_warning()
    {
        var tempDir = CreateTempDirectory();
        var dbPath = SqlitePaths.ResolveDatabasePath(tempDir);

        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var db = new HappyGymStatsDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();

            db.UserLogEntries.Add(new UserLogEntryEntity
            {
                PlayerId = 0,
                LogEntryId = "log-override",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                LogTypeId = 1,
                HappyUsed = 10,
                HappyBeforeTrain = 2500,
                EnergyUsed = 5,
                StrengthBefore = 500,
                StrengthIncreased = 5,
            });

            db.ModifierProvenance.Add(new ModifierProvenanceEntity
            {
                PlayerId = 0,
                LogEntryId = "log-override",
                Scope = (int)ModifierScope.Faction,
                FactionId = 99999,
                VerificationStatus = (int)VerificationStatus.Unresolved,
            });

            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
        services.AddScoped<IModifierProvenanceRepository, ModifierProvenanceRepository>();
        await using var provider = services.BuildServiceProvider();

        var cacheDir = Path.Combine(tempDir, "surfaces");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(
            Path.Combine(cacheDir, "modifier-overrides.local.json"),
            """
            {
              "overrides": [
                {
                  "scope": "faction",
                  "placeholderId": "99999",
                  "resolvedId": "321",
                  "linkTarget": "/factions/321"
                }
              ]
            }
            """);

        var writer = new SurfacesCacheWriter(provider.GetRequiredService<IServiceScopeFactory>(), cacheDir);
        await writer.WriteLatestAsync("test-v1", DateTimeOffset.UtcNow, CancellationToken.None);

        var latestPath = Path.Combine(cacheDir, "latest.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(latestPath));

        var warning = json.RootElement.GetProperty("series").GetProperty("gymCloud").GetProperty("provenanceWarnings")
            .EnumerateArray()
            .Single();

        Assert.Equal("/factions/321", warning.GetProperty("LinkTarget").GetString());
        Assert.True(warning.GetProperty("HasManualOverride").GetBoolean());
        Assert.Equal("local-manual", warning.GetProperty("ManualOverrideSource").GetString());

        var warningDiagnostics = json.RootElement.GetProperty("meta").GetProperty("provenanceWarningsDiagnostics");
        Assert.Equal(1, warningDiagnostics.GetProperty("overrideLoadedEntryCount").GetInt32());
        Assert.Empty(warningDiagnostics.GetProperty("overrideDiagnostics").EnumerateArray());
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
