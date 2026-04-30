using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class HappyGymStatsDbContextTests
{
    [Fact]
    public async Task EnsureCreated_creates_initial_sqlite_schema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tableNames = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
            .ToListAsync();

        Assert.Contains("RawUserLogs", tableNames);
        Assert.Contains("ImportCheckpoints", tableNames);
        Assert.Contains("ImportRuns", tableNames);
        Assert.Contains("DerivedGymTrains", tableNames);
        Assert.Contains("DerivedHappyEvents", tableNames);
    }

    [Fact]
    public async Task Raw_user_logs_enforce_unique_torn_log_ids()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.RawUserLogs.Add(new RawUserLogEntity
        {
            LogId = "same-log-id",
            OccurredAtUtc = DateTimeOffset.UnixEpoch,
            RawJson = "{}",
        });

        await db.SaveChangesAsync();

        db.RawUserLogs.Add(new RawUserLogEntity
        {
            LogId = "same-log-id",
            OccurredAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
            RawJson = "{}",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
