using System.Net;
using System.Text;
using HappyGymStats.Core.Fetch;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Storage;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Storage;
using HappyGymStats.Legacy.Cli.Storage;
using Microsoft.EntityFrameworkCore;
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
        Assert.True(await db.DerivedGymTrains.AsNoTracking().AnyAsync());
        Assert.True(await db.DerivedHappyEvents.AsNoTracking().AnyAsync());
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
