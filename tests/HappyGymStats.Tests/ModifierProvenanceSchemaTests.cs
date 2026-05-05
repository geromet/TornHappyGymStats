using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ModifierProvenanceSchemaTests
{
    [Fact]
    public async Task Round_trip_persists_scope_status_and_ids()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.ModifierProvenance.AddRange(
            new ModifierProvenanceEntity
            {
                PlayerId = 1,
                LogEntryId = "log-personal",
                Scope = (int)ModifierScope.Personal,
                SubjectId = 42,
                VerificationStatus = (int)VerificationStatus.Verified,
            },
            new ModifierProvenanceEntity
            {
                PlayerId = 1,
                LogEntryId = "log-faction",
                Scope = (int)ModifierScope.Faction,
                FactionId = 9001,
                VerificationStatus = (int)VerificationStatus.Unresolved,
            },
            new ModifierProvenanceEntity
            {
                PlayerId = 1,
                LogEntryId = "log-company",
                Scope = (int)ModifierScope.Company,
                CompanyId = 5555,
                VerificationStatus = (int)VerificationStatus.Unavailable,
            });

        await db.SaveChangesAsync();

        var rows = await db.ModifierProvenance
            .OrderBy(r => r.Scope)
            .ToListAsync();

        Assert.Equal(3, rows.Count);

        var personal = rows.Single(r => r.Scope == (int)ModifierScope.Personal);
        Assert.Equal(42, personal.SubjectId);
        Assert.Equal((int)VerificationStatus.Verified, personal.VerificationStatus);

        var faction = rows.Single(r => r.Scope == (int)ModifierScope.Faction);
        Assert.Equal(9001, faction.FactionId);
        Assert.Equal((int)VerificationStatus.Unresolved, faction.VerificationStatus);

        var company = rows.Single(r => r.Scope == (int)ModifierScope.Company);
        Assert.Equal(5555, company.CompanyId);
        Assert.Equal((int)VerificationStatus.Unavailable, company.VerificationStatus);
    }

    [Fact]
    public async Task Unresolved_states_are_queryable_by_player()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.ModifierProvenance.AddRange(
            new ModifierProvenanceEntity
            {
                PlayerId = 1, LogEntryId = "log-1",
                Scope = (int)ModifierScope.Personal,
                SubjectId = 1,
                VerificationStatus = (int)VerificationStatus.Verified,
            },
            new ModifierProvenanceEntity
            {
                PlayerId = 1, LogEntryId = "log-2",
                Scope = (int)ModifierScope.Faction,
                FactionId = 9,
                VerificationStatus = (int)VerificationStatus.Unresolved,
            },
            new ModifierProvenanceEntity
            {
                PlayerId = 1, LogEntryId = "log-3",
                Scope = (int)ModifierScope.Company,
                CompanyId = 5,
                VerificationStatus = (int)VerificationStatus.Unresolved,
            });

        await db.SaveChangesAsync();

        var unresolved = await db.ModifierProvenance
            .Where(p => p.PlayerId == 1 && p.VerificationStatus == (int)VerificationStatus.Unresolved)
            .OrderBy(p => p.LogEntryId)
            .ToListAsync();

        Assert.Equal(2, unresolved.Count);
        Assert.Equal("log-2", unresolved[0].LogEntryId);
        Assert.Equal("log-3", unresolved[1].LogEntryId);
    }

    [Fact]
    public async Task Composite_scope_bitmask_persists_correctly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // Scope bitmask 7 = personal | faction | company
        db.ModifierProvenance.Add(new ModifierProvenanceEntity
        {
            PlayerId = 1,
            LogEntryId = "log-all",
            Scope = (int)(ModifierScope.Personal | ModifierScope.Faction | ModifierScope.Company),
            SubjectId = 1,
            FactionId = 9,
            CompanyId = 5,
            VerificationStatus = (int)VerificationStatus.Verified,
        });

        await db.SaveChangesAsync();

        var row = await db.ModifierProvenance.SingleAsync(p => p.LogEntryId == "log-all");
        Assert.Equal(7, row.Scope);
        Assert.Equal(1, row.SubjectId);
        Assert.Equal(9, row.FactionId);
        Assert.Equal(5, row.CompanyId);
    }
}
