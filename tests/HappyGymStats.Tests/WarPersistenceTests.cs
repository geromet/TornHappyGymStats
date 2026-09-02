using HappyGymStats.Core.Repositories;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarPersistenceTests
{
    private const string ScopeKey = "public-war";
    private const long WarId = 48377;

    [Fact]
    public async Task Repository_round_trips_current_war_roster_score_sample_and_heartbeat_in_utc()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new HappyGymStatsDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            IWarStateRepository repository = new WarStateRepository(db);

            var current = new WarCurrentEntity
            {
                ScopeKey = ScopeKey,
                WarId = WarId,
                FactionId = 1111,
                FactionName = "Happy Gym",
                OpponentFactionId = 2222,
                OpponentFactionName = "Rivals",
                StartedAtUtc = new DateTimeOffset(2026, 5, 9, 12, 34, 56, TimeSpan.FromHours(2)),
                EndsAtUtc = new DateTimeOffset(2026, 5, 10, 1, 2, 3, TimeSpan.FromHours(-4)),
                IsLive = true,
                ObservedAtUtc = new DateTimeOffset(2026, 5, 9, 14, 15, 16, TimeSpan.FromHours(3))
            };

            await repository.UpsertCurrentAsync(current, CancellationToken.None);
            await repository.ReplaceRosterSnapshotAsync(
                WarId,
                new[]
                {
                    new WarRosterSnapshotEntity
                    {
                        WarId = WarId,
                        FactionId = 1111,
                        FactionName = "Happy Gym",
                        MemberId = 9001,
                        MemberName = "Alpha",
                        Score = 51,
                        Chain = 9,
                        Attacks = 7,
                        StatusState = "Okay",
                        StatusUntilUtc = new DateTimeOffset(2026, 5, 9, 16, 0, 0, TimeSpan.FromHours(1)),
                        CapturedAtUtc = new DateTimeOffset(2026, 5, 9, 15, 45, 0, TimeSpan.FromHours(1))
                    },
                    new WarRosterSnapshotEntity
                    {
                        WarId = WarId,
                        FactionId = 2222,
                        FactionName = "Rivals",
                        MemberId = 9002,
                        MemberName = "Bravo",
                        Score = 37,
                        Chain = 6,
                        Attacks = 5,
                        StatusState = "Hospital",
                        StatusUntilUtc = new DateTimeOffset(2026, 5, 9, 17, 0, 0, TimeSpan.FromHours(1)),
                        CapturedAtUtc = new DateTimeOffset(2026, 5, 9, 15, 45, 0, TimeSpan.FromHours(1))
                    }
                },
                CancellationToken.None);

            await repository.AddScoreSampleAsync(
                new WarScoreSampleEntity
                {
                    WarId = WarId,
                    FactionId = 1111,
                    FactionName = "Happy Gym",
                    FactionScore = 900,
                    FactionChain = 42,
                    OpponentFactionId = 2222,
                    OpponentFactionName = "Rivals",
                    OpponentScore = 870,
                    OpponentChain = 39,
                    SampledAtUtc = new DateTimeOffset(2026, 5, 9, 15, 47, 12, TimeSpan.FromHours(5))
                },
                CancellationToken.None);

            await repository.UpsertHeartbeatAsync(
                new WarPollerHeartbeatEntity
                {
                    ScopeKey = ScopeKey,
                    Phase = "succeeded",
                    UpdatedAtUtc = new DateTimeOffset(2026, 5, 9, 15, 48, 0, TimeSpan.FromHours(-7)),
                    PollStartedAtUtc = new DateTimeOffset(2026, 5, 9, 15, 46, 0, TimeSpan.FromHours(-7)),
                    PollCompletedAtUtc = new DateTimeOffset(2026, 5, 9, 15, 48, 0, TimeSpan.FromHours(-7)),
                    RetryCount = 2,
                    LastError = "timeout while loading war report",
                    ActiveWarId = WarId,
                    StaleAfterUtc = new DateTimeOffset(2026, 5, 9, 15, 53, 0, TimeSpan.FromHours(-7)),
                    PollIntervalSeconds = 30,
                    FailureBackoffSeconds = 90
                },
                CancellationToken.None);

            await db.SaveChangesAsync();
        }

        await using (var verifyDb = new HappyGymStatsDbContext(options))
        {
            IWarStateRepository repository = new WarStateRepository(verifyDb);

            var current = await repository.GetCurrentAsync(ScopeKey, CancellationToken.None);
            Assert.NotNull(current);
            Assert.Equal(TimeSpan.Zero, current!.ObservedAtUtc.Offset);
            Assert.Equal(current.ObservedAtUtc.UtcDateTime, current.ObservedAtUtc.DateTime);
            Assert.Equal(new DateTimeOffset(2026, 5, 9, 10, 34, 56, TimeSpan.Zero), current.StartedAtUtc);
            Assert.Equal(new DateTimeOffset(2026, 5, 10, 5, 2, 3, TimeSpan.Zero), current.EndsAtUtc);

            var roster = await repository.GetRosterSnapshotAsync(WarId, CancellationToken.None);
            Assert.Equal(2, roster.Count);
            Assert.All(roster, row => Assert.Equal(TimeSpan.Zero, row.CapturedAtUtc.Offset));
            Assert.All(roster.Where(row => row.StatusUntilUtc.HasValue), row => Assert.Equal(TimeSpan.Zero, row.StatusUntilUtc!.Value.Offset));

            var sample = Assert.Single(await repository.GetScoreSamplesAsync(WarId, CancellationToken.None));
            Assert.Equal(TimeSpan.Zero, sample.SampledAtUtc.Offset);
            Assert.Equal(900, sample.FactionScore);
            Assert.Equal(870, sample.OpponentScore);

            var heartbeat = await repository.GetHeartbeatAsync(ScopeKey, CancellationToken.None);
            Assert.NotNull(heartbeat);
            Assert.Equal(TimeSpan.Zero, heartbeat!.UpdatedAtUtc.Offset);
            Assert.Equal(WarId, heartbeat.ActiveWarId);
            Assert.Equal("timeout while loading war report", heartbeat.LastError);
        }
    }

    [Fact]
    public async Task Replace_roster_snapshot_replaces_existing_rows_for_same_war_only()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        IWarStateRepository repository = new WarStateRepository(db);

        await repository.ReplaceRosterSnapshotAsync(
            WarId,
            new[]
            {
                new WarRosterSnapshotEntity
                {
                    WarId = WarId,
                    FactionId = 1111,
                    FactionName = "Happy Gym",
                    MemberId = 1,
                    MemberName = "Old",
                    CapturedAtUtc = DateTimeOffset.UtcNow
                }
            },
            CancellationToken.None);

        await repository.ReplaceRosterSnapshotAsync(
            60000,
            new[]
            {
                new WarRosterSnapshotEntity
                {
                    WarId = 60000,
                    FactionId = 3333,
                    FactionName = "Elsewhere",
                    MemberId = 2,
                    MemberName = "Keep",
                    CapturedAtUtc = DateTimeOffset.UtcNow
                }
            },
            CancellationToken.None);

        await db.SaveChangesAsync();

        await repository.ReplaceRosterSnapshotAsync(
            WarId,
            new[]
            {
                new WarRosterSnapshotEntity
                {
                    WarId = WarId,
                    FactionId = 1111,
                    FactionName = "Happy Gym",
                    MemberId = 9,
                    MemberName = "New",
                    CapturedAtUtc = DateTimeOffset.UtcNow
                }
            },
            CancellationToken.None);

        await db.SaveChangesAsync();

        var replaced = await repository.GetRosterSnapshotAsync(WarId, CancellationToken.None);
        var untouched = await repository.GetRosterSnapshotAsync(60000, CancellationToken.None);

        Assert.Single(replaced);
        Assert.Equal(9, replaced[0].MemberId);
        Assert.Single(untouched);
        Assert.Equal(2, untouched[0].MemberId);
    }

    [Fact]
    public async Task War_persistence_is_scoped_to_war_tables_and_leaves_personal_lanes_empty()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        IWarStateRepository repository = new WarStateRepository(db);

        await repository.UpsertCurrentAsync(new WarCurrentEntity { ScopeKey = ScopeKey, WarId = WarId, ObservedAtUtc = DateTimeOffset.UtcNow }, CancellationToken.None);
        await repository.ReplaceRosterSnapshotAsync(
            WarId,
            new[]
            {
                new WarRosterSnapshotEntity
                {
                    WarId = WarId,
                    FactionId = 1111,
                    FactionName = "Happy Gym",
                    MemberId = 3,
                    MemberName = "Scoped",
                    CapturedAtUtc = DateTimeOffset.UtcNow
                }
            },
            CancellationToken.None);
        await repository.AddScoreSampleAsync(
            new WarScoreSampleEntity
            {
                WarId = WarId,
                FactionId = 1111,
                FactionName = "Happy Gym",
                OpponentFactionId = 2222,
                OpponentFactionName = "Rivals",
                SampledAtUtc = DateTimeOffset.UtcNow
            },
            CancellationToken.None);
        await repository.UpsertHeartbeatAsync(new WarPollerHeartbeatEntity { ScopeKey = ScopeKey, Phase = "running", UpdatedAtUtc = DateTimeOffset.UtcNow }, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Empty(await db.ImportRuns.ToListAsync());
        Assert.Empty(await db.IdentityMap.ToListAsync());
        Assert.Empty(await db.UserLogEntries.ToListAsync());
        Assert.Empty(await db.FactionMembership.ToListAsync());

        var personalTables = new[]
        {
            "IdentityMap",
            "ImportRuns",
            "ModifierProvenance",
            "AffiliationEvents",
            "FactionIdMap",
            "FactionMembership",
            "UserLogEntries",
            "LogTypes"
        };
        var warOnlyIdentifierColumns = new[] { "WarId", "OpponentFactionId", "MemberId", "ActiveWarId" };
        foreach (var tableName in personalTables)
        {
            var columns = await ReadColumnNamesAsync(connection, tableName);
            Assert.DoesNotContain(columns, column => warOnlyIdentifierColumns.Contains(column, StringComparer.Ordinal));
        }
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";

        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }
}
