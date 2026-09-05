using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarScoutServiceTests
{
    private const long ScoutedFactionId = 222;
    private const long OtherFactionId = 111;

    [Fact]
    public async Task GetProfileAsync_returns_null_when_no_captured_history_exists_for_the_faction()
    {
        await using var scope = await TestScope.CreateAsync();

        var profile = await scope.Service.GetProfileAsync(ScoutedFactionId, CancellationToken.None);

        Assert.Null(profile);
    }

    [Fact]
    public async Task GetProfileAsync_aggregates_wars_and_report_members_regardless_of_which_side_the_faction_played()
    {
        await using var scope = await TestScope.CreateAsync();

        await SeedCapturedWarAsync(scope.Db, warId: 1, factionId: ScoutedFactionId, opponentFactionId: OtherFactionId);
        await SeedCapturedWarAsync(scope.Db, warId: 2, factionId: OtherFactionId, opponentFactionId: ScoutedFactionId);
        await SeedCapturedWarAsync(scope.Db, warId: 3, factionId: OtherFactionId, opponentFactionId: 333);

        scope.Db.RankedWarReportMembers.AddRange(
            Member(warId: 1, factionId: ScoutedFactionId, memberId: 9001, name: "Alice", score: 50, attacks: 5),
            Member(warId: 2, factionId: ScoutedFactionId, memberId: 9001, name: "Alice", score: 60, attacks: 5));
        await scope.Db.SaveChangesAsync();

        var profile = await scope.Service.GetProfileAsync(ScoutedFactionId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(2, profile.TotalWarsObserved);
        var alice = Assert.Single(profile.Members);
        Assert.Equal(2, alice.WarsParticipated);
        Assert.Equal(110, alice.TotalScore);
    }

    [Fact]
    public async Task GetProfileAsync_resolves_faction_name_from_the_most_recently_captured_report_member_row()
    {
        await using var scope = await TestScope.CreateAsync();

        await SeedCapturedWarAsync(scope.Db, warId: 1, factionId: ScoutedFactionId, opponentFactionId: OtherFactionId, factionName: "Old Name");

        var member = Member(warId: 1, factionId: ScoutedFactionId, memberId: 9001, name: "Alice", score: 10, attacks: 1);
        member.FactionName = "New Name";
        scope.Db.RankedWarReportMembers.Add(member);
        await scope.Db.SaveChangesAsync();

        var profile = await scope.Service.GetProfileAsync(ScoutedFactionId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("New Name", profile.FactionName);
    }

    [Fact]
    public async Task GetProfileAsync_rejects_non_positive_faction_ids()
    {
        await using var scope = await TestScope.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => scope.Service.GetProfileAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task GetProfileAsync_surfaces_faction_level_record_pace_and_concentration_through_the_repository()
    {
        await using var scope = await TestScope.CreateAsync();

        scope.Db.RankedWarHistory.AddRange(
            FullWar(warId: 1, factionId: ScoutedFactionId, opponentFactionId: OtherFactionId,
                factionScore: 6000, opponentScore: 4000, winnerFactionId: ScoutedFactionId, durationHours: 6),
            FullWar(warId: 2, factionId: OtherFactionId, opponentFactionId: ScoutedFactionId,
                factionScore: 5000, opponentScore: 3000, winnerFactionId: OtherFactionId, durationHours: 5));
        scope.Db.RankedWarReportMembers.AddRange(
            Member(1, ScoutedFactionId, 9001, "Ace", score: 5000, attacks: 50),
            Member(1, ScoutedFactionId, 9002, "Filler", score: 1000, attacks: 40),
            Member(2, ScoutedFactionId, 9001, "Ace", score: 2000, attacks: 30),
            Member(2, ScoutedFactionId, 9002, "Filler", score: 1000, attacks: 30));
        await scope.Db.SaveChangesAsync();

        var profile = await scope.Service.GetProfileAsync(ScoutedFactionId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(2, profile!.WarsWithKnownOutcome);
        Assert.Equal(0.5m, profile.WinRate);
        Assert.Equal(4500, profile.TypicalTargetScore);
        Assert.Equal(800m, profile.PointsPerHour);
        Assert.Equal(1m, profile.Top5ScoreShare);
    }

    [Fact]
    public async Task GetProfileAsync_exposes_only_sanitized_latest_backfill_coverage()
    {
        await using var scope = await TestScope.CreateAsync();
        await SeedCapturedWarAsync(scope.Db, warId: 1, factionId: ScoutedFactionId, opponentFactionId: OtherFactionId);

        var updatedAt = new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
        scope.Db.RankedWarHistoryBackfillState.Add(new RankedWarHistoryBackfillStateEntity
        {
            ScopeKey = "public-war",
            Status = RankedWarHistoryBackfillStatus.Completed,
            Phase = RankedWarHistoryBackfillPhase.Idle,
            NextHistoryPageUrl = "https://operator-only.invalid/retry",
            PagesProcessed = 42,
            ReportsProcessed = 137,
            RetryCount = 3,
            LastFailureCategory = "RateLimited",
            LastErrorMessage = "operator-only diagnostic",
            LastSuccessAtUtc = updatedAt.AddMinutes(-2),
            UpdatedAtUtc = updatedAt,
            CreatedAtUtc = updatedAt.AddDays(-1),
        });
        await scope.Db.SaveChangesAsync();

        var profile = await scope.Service.GetProfileAsync(ScoutedFactionId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(RankedWarHistoryBackfillStatus.Completed, profile!.Evidence.BackfillStatus);
        Assert.Equal(42, profile.Evidence.PagesProcessed);
        Assert.Equal(137, profile.Evidence.ReportsProcessed);
        Assert.Equal(updatedAt, profile.Evidence.UpdatedAtUtc);
        Assert.True(profile.Evidence.IsComplete);
        Assert.DoesNotContain("Error", typeof(WarScoutEvidenceMetadata).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("Retry", typeof(WarScoutEvidenceMetadata).GetProperties().Select(property => property.Name));
    }

    private static RankedWarHistoryEntity FullWar(
        long warId, long factionId, long opponentFactionId,
        int factionScore, int opponentScore, long winnerFactionId, double durationHours)
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(warId);
        return new RankedWarHistoryEntity
        {
            WarId = warId,
            FactionId = factionId,
            FactionName = "Chain Breakers",
            OpponentFactionId = opponentFactionId,
            OpponentFactionName = "Opponent",
            StartedAtUtc = start,
            EndedAtUtc = start.AddHours(durationHours),
            WinnerFactionId = winnerFactionId,
            FactionScore = factionScore,
            OpponentScore = opponentScore,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IngestedAtUtc = DateTimeOffset.UtcNow,
            ReportCapturedAtUtc = DateTimeOffset.UtcNow,
            ReportIngestedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static async Task SeedCapturedWarAsync(
        HappyGymStatsDbContext db,
        long warId,
        long factionId,
        long opponentFactionId,
        string factionName = "Chain Breakers")
    {
        db.RankedWarHistory.Add(new RankedWarHistoryEntity
        {
            WarId = warId,
            FactionId = factionId,
            FactionName = factionName,
            OpponentFactionId = opponentFactionId,
            OpponentFactionName = "Opponent",
            StartedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(warId),
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IngestedAtUtc = DateTimeOffset.UtcNow,
            ReportCapturedAtUtc = DateTimeOffset.UtcNow,
            ReportIngestedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static RankedWarReportMemberEntity Member(long warId, long factionId, long memberId, string name, int score, int attacks)
        => new()
        {
            WarId = warId,
            FactionId = factionId,
            FactionName = "Chain Breakers",
            MemberId = memberId,
            MemberName = name,
            Score = score,
            Attacks = attacks,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IngestedAtUtc = DateTimeOffset.UtcNow,
        };

    private sealed class TestScope : IAsyncDisposable
    {
        private TestScope(SqliteConnection connection, HappyGymStatsDbContext db, WarScoutService service)
        {
            Connection = connection;
            Db = db;
            Service = service;
        }

        public SqliteConnection Connection { get; }
        public HappyGymStatsDbContext Db { get; }
        public WarScoutService Service { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var db = new HappyGymStatsDbContext(new DbContextOptionsBuilder<HappyGymStatsDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            IWarHistoryRepository repository = new WarHistoryRepository(db);
            IRankedWarHistoryBackfillStateRepository backfillRepository = new RankedWarHistoryBackfillStateRepository(db);
            return new TestScope(connection, db, new WarScoutService(repository, backfillRepository));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
