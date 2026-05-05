using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq;
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

        Assert.Contains("UserLogEntries", tableNames);
        Assert.Contains("AffiliationEvents", tableNames);
        Assert.Contains("ModifierProvenance", tableNames);
        Assert.Contains("ImportRuns", tableNames);
        Assert.Contains("LogTypes", tableNames);

        var provenanceColumns = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ModifierProvenance') ORDER BY name")
            .ToListAsync();

        Assert.Contains("PlayerId", provenanceColumns);
        Assert.Contains("LogEntryId", provenanceColumns);
        Assert.Contains("Scope", provenanceColumns);
        Assert.Contains("SubjectId", provenanceColumns);
        Assert.Contains("FactionId", provenanceColumns);
        Assert.Contains("CompanyId", provenanceColumns);
        Assert.Contains("VerificationStatus", provenanceColumns);

        var provenanceIndexes = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'ModifierProvenance' ORDER BY name")
            .ToListAsync();

        Assert.Contains("IX_ModifierProvenance_PlayerId_VerificationStatus", provenanceIndexes);
    }

    [Fact]
    public async Task User_log_entries_enforce_composite_pk_uniqueness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.UserLogEntries.Add(new UserLogEntryEntity
        {
            PlayerId = 1,
            LogEntryId = "same-log-id",
            OccurredAtUtc = DateTimeOffset.UnixEpoch,
            LogTypeId = 1,
        });

        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO UserLogEntries (PlayerId, LogEntryId, OccurredAtUtc, LogTypeId) VALUES (1, 'same-log-id', '2026-01-01', 2)"));
    }

    [Fact]
    public async Task Modifier_provenance_enforces_composite_pk_uniqueness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.ModifierProvenance.Add(new ModifierProvenanceEntity
        {
            PlayerId = 1,
            LogEntryId = "log-1",
            Scope = 1,
            SubjectId = 42,
            VerificationStatus = 1,
        });

        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ModifierProvenance (PlayerId, LogEntryId, Scope, SubjectId, VerificationStatus) VALUES (1, 'log-1', 1, 99, 2)"));
    }

    [Fact]
    public async Task Affiliation_events_enforce_composite_pk_uniqueness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.AffiliationEvents.Add(new AffiliationEventEntity
        {
            PlayerId = 1,
            SourceLogEntryId = "log-1",
            LogTypeId = 100,
            Scope = AffiliationScope.Faction,
            AffiliationId = 9001,
        });

        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO AffiliationEvents (PlayerId, SourceLogEntryId, LogTypeId, Scope, AffiliationId) VALUES (1, 'log-1', 101, 4, 9002)"));
    }
}
