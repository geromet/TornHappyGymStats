using HappyGymStats.Core.Repositories;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarHistoryPersistenceTests
{
    [Fact]
    public async Task UpsertWarAsync_round_trips_utc_and_nullable_end_time()
    {
        await using var scope = await TestScope.CreateAsync();
        var repository = scope.Repository;

        var capturedAt = new DateTimeOffset(2026, 9, 2, 18, 30, 0, TimeSpan.FromHours(2));
        var ingestedAt = capturedAt.AddMinutes(3);
        var startedAt = new DateTimeOffset(2026, 9, 1, 20, 0, 0, TimeSpan.FromHours(-4));

        await repository.UpsertWarAsync(new RankedWarHistoryEntity
        {
            WarId = 48377,
            FactionId = 111,
            FactionName = "Happy Gym",
            OpponentFactionId = 222,
            OpponentFactionName = "Chain Breakers",
            StartedAtUtc = startedAt,
            EndedAtUtc = null,
            WinnerFactionId = null,
            FactionScore = 128,
            FactionChain = 42,
            OpponentScore = 111,
            OpponentChain = 33,
            Status = null,
            CapturedAtUtc = capturedAt,
            IngestedAtUtc = ingestedAt,
        }, CancellationToken.None);

        await scope.Db.SaveChangesAsync();

        var stored = await repository.GetWarAsync(48377, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(startedAt.ToUniversalTime(), stored.StartedAtUtc);
        Assert.Null(stored.EndedAtUtc);
        Assert.Equal(capturedAt.ToUniversalTime(), stored.CapturedAtUtc);
        Assert.Equal(ingestedAt.ToUniversalTime(), stored.IngestedAtUtc);
    }

    [Fact]
    public async Task UpsertWarAsync_is_idempotent_by_war_id()
    {
        await using var scope = await TestScope.CreateAsync();
        var repository = scope.Repository;

        await repository.UpsertWarAsync(CreateWar(capturedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10), ingestedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-9)), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        await repository.UpsertWarAsync(CreateWar(
            factionName: "Happy Gym Prime",
            endedAtUtc: DateTimeOffset.UtcNow,
            winnerFactionId: 111,
            capturedAtUtc: DateTimeOffset.UtcNow,
            ingestedAtUtc: DateTimeOffset.UtcNow.AddMinutes(1)), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        Assert.Equal(1, await scope.Db.RankedWarHistory.CountAsync());

        var stored = await repository.GetWarAsync(48377, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("Happy Gym Prime", stored.FactionName);
        Assert.Equal(111, stored.WinnerFactionId);
        Assert.NotNull(stored.EndedAtUtc);
    }

    [Fact]
    public async Task ReplaceReportMembersAsync_replaces_rows_atomically_and_updates_capture_state()
    {
        await using var scope = await TestScope.CreateAsync();
        var repository = scope.Repository;

        await repository.UpsertWarAsync(CreateWar(), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        Assert.False(await repository.HasCapturedReportAsync(48377, CancellationToken.None));

        var firstCapture = DateTimeOffset.UtcNow.AddMinutes(-5);
        var firstIngest = firstCapture.AddMinutes(1);
        await repository.ReplaceReportMembersAsync(48377, firstCapture, firstIngest, [
            CreateMember(111, 1001, "Alice", isIdleAttacker: false),
            CreateMember(111, 1002, "Bob", isIdleAttacker: true),
            CreateMember(222, 2001, "Mallory", isIdleAttacker: false)
        ], CancellationToken.None);

        Assert.True(await repository.HasCapturedReportAsync(48377, CancellationToken.None));
        Assert.Equal(3, await scope.Db.RankedWarReportMembers.CountAsync());

        var secondCapture = DateTimeOffset.UtcNow;
        var secondIngest = secondCapture.AddMinutes(1);
        await repository.ReplaceReportMembersAsync(48377, secondCapture, secondIngest, [
            CreateMember(111, 1001, "Alice", score: 60, isIdleAttacker: false),
            CreateMember(222, 2001, "Mallory", score: 44, isIdleAttacker: false),
            CreateMember(222, 2002, "Trent", score: 12, isIdleAttacker: true)
        ], CancellationToken.None);

        Assert.Equal(3, await scope.Db.RankedWarReportMembers.CountAsync());

        var happyGymMembers = await repository.GetReportMembersAsync(48377, 111, CancellationToken.None);
        Assert.Single(happyGymMembers);
        Assert.Equal(60, happyGymMembers[0].Score);
        Assert.False(happyGymMembers[0].IsIdleAttacker);

        var chainBreakersMembers = await repository.GetReportMembersAsync(48377, 222, CancellationToken.None);
        Assert.Equal(2, chainBreakersMembers.Count);
        Assert.Contains(chainBreakersMembers, member => member.MemberId == 2002 && member.IsIdleAttacker);

        var war = await repository.GetWarAsync(48377, CancellationToken.None);
        Assert.NotNull(war);
        Assert.Equal(secondCapture.ToUniversalTime(), war.ReportCapturedAtUtc);
        Assert.Equal(secondIngest.ToUniversalTime(), war.ReportIngestedAtUtc);
    }

    [Fact]
    public async Task ReplaceReportMembersAsync_rejects_invalid_member_before_partial_write()
    {
        await using var scope = await TestScope.CreateAsync();
        var repository = scope.Repository;

        await repository.UpsertWarAsync(CreateWar(), CancellationToken.None);
        await scope.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ReplaceReportMembersAsync(
            48377,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [
                CreateMember(111, 1001, "Alice"),
                CreateMember(111, 0, "Broken")
            ],
            CancellationToken.None));

        Assert.Equal(nameof(RankedWarReportMemberEntity.MemberId), ex.ParamName);
        Assert.Equal(0, await scope.Db.RankedWarReportMembers.CountAsync());

        var war = await repository.GetWarAsync(48377, CancellationToken.None);
        Assert.NotNull(war);
        Assert.Null(war.ReportCapturedAtUtc);
        Assert.Null(war.ReportIngestedAtUtc);
    }

    [Fact]
    public async Task ReplaceReportMembersAsync_requires_existing_war_history_row()
    {
        await using var scope = await TestScope.CreateAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Repository.ReplaceReportMembersAsync(
            48377,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [CreateMember(111, 1001, "Alice")],
            CancellationToken.None));

        Assert.Contains("war history row exists", ex.Message);
        Assert.Equal(0, await scope.Db.RankedWarReportMembers.CountAsync());
    }

    [Fact]
    public async Task DbContext_configures_history_indexes_for_future_queries()
    {
        await using var scope = await TestScope.CreateAsync();

        var historyEntity = scope.Db.Model.FindEntityType(typeof(RankedWarHistoryEntity));
        Assert.NotNull(historyEntity);
        Assert.Contains(historyEntity!.GetIndexes(), index => Matches(index, nameof(RankedWarHistoryEntity.FactionId), nameof(RankedWarHistoryEntity.StartedAtUtc)));
        Assert.Contains(historyEntity.GetIndexes(), index => Matches(index, nameof(RankedWarHistoryEntity.OpponentFactionId), nameof(RankedWarHistoryEntity.StartedAtUtc)));
        Assert.Contains(historyEntity.GetIndexes(), index => Matches(index, nameof(RankedWarHistoryEntity.EndedAtUtc)));

        var memberEntity = scope.Db.Model.FindEntityType(typeof(RankedWarReportMemberEntity));
        Assert.NotNull(memberEntity);
        Assert.Contains(memberEntity!.GetIndexes(), index => Matches(index, nameof(RankedWarReportMemberEntity.WarId), nameof(RankedWarReportMemberEntity.FactionId)));
        Assert.Contains(memberEntity.GetIndexes(), index => Matches(index, nameof(RankedWarReportMemberEntity.FactionId), nameof(RankedWarReportMemberEntity.MemberId)));
        Assert.Contains(memberEntity.GetIndexes(), index => Matches(index, nameof(RankedWarReportMemberEntity.MemberId)));
    }

    private static bool Matches(Microsoft.EntityFrameworkCore.Metadata.IReadOnlyIndex index, params string[] properties)
        => index.Properties.Select(property => property.Name).SequenceEqual(properties);

    private static RankedWarHistoryEntity CreateWar(
        string factionName = "Happy Gym",
        DateTimeOffset? endedAtUtc = null,
        long? winnerFactionId = null,
        DateTimeOffset? capturedAtUtc = null,
        DateTimeOffset? ingestedAtUtc = null)
        => new()
        {
            WarId = 48377,
            FactionId = 111,
            FactionName = factionName,
            OpponentFactionId = 222,
            OpponentFactionName = "Chain Breakers",
            StartedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            EndedAtUtc = endedAtUtc,
            WinnerFactionId = winnerFactionId,
            FactionScore = 128,
            FactionChain = 42,
            OpponentScore = 111,
            OpponentChain = 33,
            Status = endedAtUtc is null ? null : "finished",
            CapturedAtUtc = capturedAtUtc ?? new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            IngestedAtUtc = ingestedAtUtc ?? new DateTimeOffset(2026, 9, 2, 0, 5, 0, TimeSpan.Zero),
        };

    private static RankedWarReportMemberEntity CreateMember(
        long factionId,
        long memberId,
        string memberName,
        int score = 50,
        bool isIdleAttacker = false)
        => new()
        {
            WarId = 48377,
            FactionId = factionId,
            FactionName = factionId == 111 ? "Happy Gym" : "Chain Breakers",
            MemberId = memberId,
            MemberName = memberName,
            Score = score,
            Chain = 10,
            Attacks = 3,
            StatusState = isIdleAttacker ? "Okay" : "Hospital",
            StatusUntilUtc = isIdleAttacker ? null : new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero),
            IsIdleAttacker = isIdleAttacker,
            CapturedAtUtc = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            IngestedAtUtc = new DateTimeOffset(2026, 9, 2, 0, 5, 0, TimeSpan.Zero),
        };

    private sealed class TestScope : IAsyncDisposable
    {
        private TestScope(SqliteConnection connection, HappyGymStatsDbContext db, IWarHistoryRepository repository)
        {
            Connection = connection;
            Db = db;
            Repository = repository;
        }

        public SqliteConnection Connection { get; }
        public HappyGymStatsDbContext Db { get; }
        public IWarHistoryRepository Repository { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new HappyGymStatsDbContext(options);
            await db.Database.EnsureCreatedAsync();

            return new TestScope(connection, db, new WarHistoryRepository(db));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
