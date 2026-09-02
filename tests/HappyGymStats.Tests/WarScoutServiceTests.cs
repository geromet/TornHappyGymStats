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

        // Scouted faction played as the "faction" side of war 1 and the "opponent" side of war 2.
        await SeedCapturedWarAsync(scope.Db, warId: 1, factionId: ScoutedFactionId, opponentFactionId: OtherFactionId);
        await SeedCapturedWarAsync(scope.Db, warId: 2, factionId: OtherFactionId, opponentFactionId: ScoutedFactionId);
        // An unrelated war the scouted faction had no part in must not be included.
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
            return new TestScope(connection, db, new WarScoutService(repository));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
