using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Fast schema-contract tests for the SQLite provider.
/// </summary>
public sealed class SqliteHappyGymStatsDbContextTests
{
    [Fact]
    public async Task EnsureCreated_creates_current_sqlite_schema()
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

        Assert.Contains("IdentityMap", tableNames);
        Assert.Contains("ImportRuns", tableNames);
        Assert.Contains("ModifierProvenance", tableNames);
        Assert.Contains("AffiliationEvents", tableNames);
        Assert.Contains("FactionIdMap", tableNames);
        Assert.Contains("FactionMembership", tableNames);
        Assert.Contains("UserLogEntries", tableNames);
        Assert.Contains("LogTypes", tableNames);
    }

    [Fact]
    public async Task Sqlite_user_log_entries_enforce_composite_primary_key()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var anonymousId = Guid.NewGuid();

        var first = new UserLogEntryEntity
        {
            AnonymousId = anonymousId,
            LogEntryId = "log-1",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            LogTypeId = 1
        };

        db.UserLogEntries.Add(first);

        await db.SaveChangesAsync();

        // EF Core 10's change tracker throws an identity-map conflict when a second
        // instance with the same client-set composite key is added, which would mask
        // the constraint we are testing here. Detach the first instance so the second
        // insert actually reaches SQLite and violates the composite primary key.
        db.Entry(first).State = EntityState.Detached;

        db.UserLogEntries.Add(new UserLogEntryEntity
        {
            AnonymousId = anonymousId,
            LogEntryId = "log-1",
            OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            LogTypeId = 2
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sqlite_modifier_provenance_enforces_composite_primary_key()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var anonymousId = Guid.NewGuid();

        var first = new ModifierProvenanceEntity
        {
            AnonymousId = anonymousId,
            LogEntryId = "log-1",
            Scope = 1,
            SubjectId = 123,
            VerificationStatus = 1
        };

        db.ModifierProvenance.Add(first);

        await db.SaveChangesAsync();

        // See Sqlite_user_log_entries_enforce_composite_primary_key: detach the first
        // instance so the duplicate insert reaches SQLite and violates the composite PK.
        db.Entry(first).State = EntityState.Detached;

        db.ModifierProvenance.Add(new ModifierProvenanceEntity
        {
            AnonymousId = anonymousId,
            LogEntryId = "log-1",
            Scope = 1,
            SubjectId = 456,
            VerificationStatus = 2
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sqlite_modifier_provenance_allows_distinct_scope_for_same_log()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var anonymousId = Guid.NewGuid();

        db.ModifierProvenance.AddRange(
            new ModifierProvenanceEntity
            {
                AnonymousId = anonymousId,
                LogEntryId = "log-1",
                Scope = 1,
                SubjectId = 123,
                VerificationStatus = 1
            },
            new ModifierProvenanceEntity
            {
                AnonymousId = anonymousId,
                LogEntryId = "log-1",
                Scope = 2,
                FactionId = 999,
                VerificationStatus = 2
            });

        await db.SaveChangesAsync();

        var rows = await db.ModifierProvenance
            .Where(x => x.AnonymousId == anonymousId && x.LogEntryId == "log-1")
            .ToListAsync();

        Assert.Equal(2, rows.Count);
    }
}
