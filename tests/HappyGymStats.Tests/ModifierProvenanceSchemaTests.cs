using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ModifierProvenanceSchemaTests
{
    [Fact]
    public async Task Round_trip_persists_scope_status_reason_and_interval_shapes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.DerivedGymTrains.AddRange(
            Train("train-personal", "2026-01-01T00:00:00Z"),
            Train("train-faction", "2026-01-01T01:00:00Z"),
            Train("train-company", "2026-01-01T02:00:00Z"));
        await db.SaveChangesAsync();

        var personalFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(+2));
        var personalTo = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.FromHours(+2));

        db.ModifierProvenance.AddRange(
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-personal",
                Scope = "personal",
                SubjectId = "u-1",
                ValidFromUtc = personalFrom,
                ValidToUtc = personalTo,
                VerificationStatus = "verified",
                VerificationReasonCode = "source-log",
                VerificationDetails = "matched direct user evidence",
            },
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-faction",
                Scope = "faction",
                FactionId = "f-1",
                ValidFromUtc = DateTimeOffset.Parse("2026-01-01T01:00:00Z"),
                ValidToUtc = null,
                VerificationStatus = "unresolved",
                VerificationReasonCode = "missing-faction-record",
                VerificationDetails = "faction payload unavailable",
            },
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-company",
                Scope = "company",
                CompanyId = "c-1",
                ValidFromUtc = DateTimeOffset.Parse("2026-01-01T02:00:00Z"),
                ValidToUtc = DateTimeOffset.Parse("2026-01-01T03:30:00Z"),
                VerificationStatus = "unavailable",
                VerificationReasonCode = "company-api-outage",
                VerificationDetails = "provider timeout",
            });

        await db.SaveChangesAsync();

        var rows = await db.ModifierProvenance
            .OrderBy(r => r.Scope)
            .ToListAsync();

        Assert.Equal(3, rows.Count);

        var company = rows.Single(r => r.Scope == "company");
        Assert.Equal("unavailable", company.VerificationStatus);
        Assert.Equal("company-api-outage", company.VerificationReasonCode);
        Assert.NotNull(company.ValidToUtc);

        var faction = rows.Single(r => r.Scope == "faction");
        Assert.Equal("unresolved", faction.VerificationStatus);
        Assert.Equal("missing-faction-record", faction.VerificationReasonCode);
        Assert.Null(faction.ValidToUtc);

        var personal = rows.Single(r => r.Scope == "personal");
        Assert.Equal("verified", personal.VerificationStatus);
        Assert.Equal("source-log", personal.VerificationReasonCode);
        Assert.Equal(DateTimeOffset.Parse("2025-12-31T22:00:00Z"), personal.ValidFromUtc);
        Assert.Equal(DateTimeOffset.Parse("2025-12-31T23:00:00Z"), personal.ValidToUtc);
    }

    [Fact]
    public async Task Unresolved_states_are_queryable_for_operational_diagnostics()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.DerivedGymTrains.AddRange(
            Train("train-1", "2026-01-01T00:00:00Z"),
            Train("train-2", "2026-01-01T00:10:00Z"),
            Train("train-3", "2026-01-01T00:20:00Z"));
        await db.SaveChangesAsync();

        db.ModifierProvenance.AddRange(
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-1",
                Scope = "personal",
                SubjectId = "u-1",
                ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                VerificationStatus = "verified",
                VerificationReasonCode = "source-log",
            },
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-2",
                Scope = "faction",
                FactionId = "f-9",
                ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:10:00Z"),
                VerificationStatus = "unresolved",
                VerificationReasonCode = "missing-faction-record",
            },
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-3",
                Scope = "company",
                CompanyId = "c-9",
                ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:20:00Z"),
                VerificationStatus = "unresolved",
                VerificationReasonCode = "company-payload-malformed",
            });

        await db.SaveChangesAsync();

        var unresolved = await db.ModifierProvenance
            .Where(p => p.VerificationStatus == "unresolved")
            .OrderBy(p => p.DerivedGymTrainLogId)
            .Select(p => new { p.DerivedGymTrainLogId, p.Scope, p.VerificationReasonCode })
            .ToListAsync();

        Assert.Equal(2, unresolved.Count);
        Assert.Equal("train-2", unresolved[0].DerivedGymTrainLogId);
        Assert.Equal("missing-faction-record", unresolved[0].VerificationReasonCode);
        Assert.Equal("train-3", unresolved[1].DerivedGymTrainLogId);
        Assert.Equal("company-payload-malformed", unresolved[1].VerificationReasonCode);
    }

    [Fact]
    public async Task Invalid_scope_or_status_is_rejected_by_schema_contract()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.DerivedGymTrains.Add(Train("train-1", "2026-01-01T00:00:00Z"));
        await db.SaveChangesAsync();

        db.ModifierProvenance.Add(new ModifierProvenanceEntity
        {
            DerivedGymTrainLogId = "train-1",
            Scope = "alliance",
            SubjectId = "u-1",
            ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            VerificationStatus = "verified",
            VerificationReasonCode = "source-log",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();

        db.ModifierProvenance.Add(new ModifierProvenanceEntity
        {
            DerivedGymTrainLogId = "train-1",
            Scope = "personal",
            SubjectId = "u-1",
            ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            VerificationStatus = "unknown",
            VerificationReasonCode = "x",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static DerivedGymTrainEntity Train(string logId, string occurredAtUtc)
        => new()
        {
            LogId = logId,
            OccurredAtUtc = DateTimeOffset.Parse(occurredAtUtc),
            HappyBeforeTrain = 100,
            HappyAfterTrain = 110,
            HappyUsed = 10,
            RegenTicksApplied = 0,
            RegenHappyGained = 0,
            ClampedToMax = false,
        };
}
