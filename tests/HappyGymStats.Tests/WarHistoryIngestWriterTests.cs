using System.Text.Json;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarHistoryIngestWriterTests
{
    [Fact]
    public async Task WriteHistoryPageAsync_and_WriteReportAsync_are_idempotent_and_refresh_capture_timestamps()
    {
        await using var scope = await TestScope.CreateAsync();
        var writer = scope.Writer;
        var page = DeserializeFixture<RankedWarHistoryPageResponse>("tests/fixtures/war/v2-warfareranked-page.json");
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/v2-ranked-war-report-48377.json");

        var firstCapture = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(-4));
        var firstIngest = firstCapture.AddMinutes(2);
        var secondCapture = firstCapture.AddHours(4);
        var secondIngest = secondCapture.AddMinutes(1);

        await writer.WriteHistoryPageAsync(page, firstCapture, firstIngest, CancellationToken.None);
        await writer.WriteReportAsync(report, firstCapture, firstIngest, CancellationToken.None);

        var initialWarCount = await scope.Db.RankedWarHistory.CountAsync();
        var initialMemberCount = await scope.Db.RankedWarReportMembers.CountAsync();

        await writer.WriteHistoryPageAsync(page, secondCapture, secondIngest, CancellationToken.None);
        await writer.WriteReportAsync(report, secondCapture, secondIngest, CancellationToken.None);

        Assert.Equal(page.Wars.Count, initialWarCount);
        Assert.Equal(report.Factions.Sum(f => f.Members.Count), initialMemberCount);
        Assert.Equal(initialWarCount, await scope.Db.RankedWarHistory.CountAsync());
        Assert.Equal(initialMemberCount, await scope.Db.RankedWarReportMembers.CountAsync());

        var war = await scope.Repository.GetWarAsync(report.War.WarId, CancellationToken.None);
        Assert.NotNull(war);
        Assert.Equal(secondCapture.ToUniversalTime(), war!.CapturedAtUtc);
        Assert.Equal(secondIngest.ToUniversalTime(), war.IngestedAtUtc);
        Assert.Equal(secondCapture.ToUniversalTime(), war.ReportCapturedAtUtc);
        Assert.Equal(secondIngest.ToUniversalTime(), war.ReportIngestedAtUtc);

        var idleAttackerIds = report.IdleAttackers.ToHashSet();
        var storedMembers = await scope.Db.RankedWarReportMembers
            .Where(m => m.WarId == report.War.WarId)
            .OrderBy(m => m.FactionId)
            .ThenBy(m => m.MemberId)
            .ToListAsync();

        Assert.NotEmpty(storedMembers);
        Assert.All(storedMembers, member =>
        {
            Assert.Equal(secondCapture.ToUniversalTime(), member.CapturedAtUtc);
            Assert.Equal(secondIngest.ToUniversalTime(), member.IngestedAtUtc);
            Assert.Equal(idleAttackerIds.Contains(member.MemberId), member.IsIdleAttacker);
        });
    }

    [Fact]
    public async Task WriteReportAsync_rejects_invalid_member_identity_before_partial_writes()
    {
        await using var scope = await TestScope.CreateAsync();
        var writer = scope.Writer;
        var page = DeserializeFixture<RankedWarHistoryPageResponse>("tests/fixtures/war/v2-warfareranked-page.json");
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/v2-ranked-war-report-48377.json");

        var capturedAt = DateTimeOffset.UtcNow;
        var ingestedAt = capturedAt.AddMinutes(1);
        await writer.WriteHistoryPageAsync(page, capturedAt, ingestedAt, CancellationToken.None);

        var invalidFirstFaction = report.Factions[0] with
        {
            Members =
            [
                report.Factions[0].Members[0] with { UserId = 0 },
                .. report.Factions[0].Members.Skip(1),
            ],
        };

        var invalidReport = report with
        {
            Factions =
            [
                invalidFirstFaction,
                .. report.Factions.Skip(1),
            ],
        };

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => writer.WriteReportAsync(
            invalidReport,
            capturedAt.AddHours(1),
            ingestedAt.AddHours(1),
            CancellationToken.None));

        Assert.Equal("UserId", ex.ParamName);
        Assert.Empty(await scope.Db.RankedWarReportMembers.ToListAsync());

        var war = await scope.Repository.GetWarAsync(report.War.WarId, CancellationToken.None);
        Assert.NotNull(war);
        Assert.Null(war!.ReportCapturedAtUtc);
        Assert.Null(war.ReportIngestedAtUtc);
    }

    [Fact]
    public async Task WriteReportAsync_requires_existing_history_war()
    {
        await using var scope = await TestScope.CreateAsync();
        var writer = scope.Writer;
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/v2-ranked-war-report-48377.json");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteReportAsync(
            report,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            CancellationToken.None));

        Assert.Contains("must exist before persisting report members", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHost_registers_war_history_ingest_writer()
    {
        using var host = HappyGymStats.WarPoller.Program.BuildHost(
            configureBuilder: builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // The key Program.ResolveConnectionString actually reads. It used to
                    // say "Default", which that method never looks at — the test passed
                    // only because the API project's appsettings.json lands in the test
                    // output directory and supplies a real-looking one. On a clean CI
                    // checkout there was nothing to fall back on and this failed.
                    ["ConnectionStrings:HappyGymStats"] = "Host=localhost;Database=happy-gym-stats-tests;Username=test;Password=test",
                    ["WarPoller:ApiKey"] = "test-key",
                    ["WarPoller:FactionId"] = "123",
                });
            });

        using var scope = host.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWarHistoryIngestWriter>();
        Assert.IsType<WarHistoryIngestWriter>(writer);
    }

    private static T DeserializeFixture<T>(string path)
    {
        var fullPath = Path.GetFullPath(path, ResolveRepositoryRoot());
        return JsonSerializer.Deserialize<T>(File.ReadAllText(fullPath), WarEndpointJson.SerializerOptions)
            ?? throw new InvalidOperationException($"Fixture '{path}' could not be deserialized.");
    }

    private static string ResolveRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestScope(SqliteConnection connection, HappyGymStatsDbContext db)
        {
            _connection = connection;
            Db = db;
            Repository = new WarHistoryRepository(db);
            Writer = new WarHistoryIngestWriter(Repository, db);
        }

        public HappyGymStatsDbContext Db { get; }
        public IWarHistoryRepository Repository { get; }
        public IWarHistoryIngestWriter Writer { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new HappyGymStatsDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new TestScope(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
