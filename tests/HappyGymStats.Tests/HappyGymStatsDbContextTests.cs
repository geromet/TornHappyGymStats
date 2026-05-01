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

        Assert.Contains("RawUserLogs", tableNames);
        Assert.Contains("ImportCheckpoints", tableNames);
        Assert.Contains("ImportRuns", tableNames);
        Assert.Contains("DerivedGymTrains", tableNames);
        Assert.Contains("DerivedHappyEvents", tableNames);
        Assert.Contains("ModifierProvenance", tableNames);
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

    [Fact]
    public async Task Modifier_provenance_rejects_invalid_status_or_missing_scope_identifier()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.DerivedGymTrains.Add(new DerivedGymTrainEntity
        {
            LogId = "train-1",
            OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            HappyBeforeTrain = 100,
            HappyAfterTrain = 110,
            HappyUsed = 10,
            RegenTicksApplied = 0,
            RegenHappyGained = 0,
            ClampedToMax = false,
        });
        await db.SaveChangesAsync();

        db.ModifierProvenance.Add(new ModifierProvenanceEntity
        {
            DerivedGymTrainLogId = "train-1",
            Scope = "personal",
            SubjectId = "", // invalid: empty for personal
            ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ValidToUtc = DateTimeOffset.Parse("2026-01-01T01:00:00Z"),
            VerificationStatus = "verified",
            VerificationReasonCode = "ok",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();

        db.ModifierProvenance.Add(new ModifierProvenanceEntity
        {
            DerivedGymTrainLogId = "train-1",
            Scope = "faction",
            FactionId = "f123",
            ValidFromUtc = DateTimeOffset.Parse("2026-01-01T01:00:00Z"),
            ValidToUtc = DateTimeOffset.Parse("2026-01-01T02:00:00Z"),
            VerificationStatus = "bad-status",
            VerificationReasonCode = "malformed",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Modifier_provenance_allows_adjacent_time_windows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.DerivedGymTrains.AddRange(
            new DerivedGymTrainEntity
            {
                LogId = "train-a",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                HappyBeforeTrain = 100,
                HappyAfterTrain = 110,
                HappyUsed = 10,
                RegenTicksApplied = 0,
                RegenHappyGained = 0,
                ClampedToMax = false,
            },
            new DerivedGymTrainEntity
            {
                LogId = "train-b",
                OccurredAtUtc = DateTimeOffset.Parse("2026-01-01T02:00:00Z"),
                HappyBeforeTrain = 110,
                HappyAfterTrain = 120,
                HappyUsed = 10,
                RegenTicksApplied = 0,
                RegenHappyGained = 0,
                ClampedToMax = false,
            });

        await db.SaveChangesAsync();

        var boundary = DateTimeOffset.Parse("2026-01-01T01:00:00Z");
        db.ModifierProvenance.AddRange(
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-a",
                Scope = "personal",
                SubjectId = "u1",
                ValidFromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                ValidToUtc = boundary,
                VerificationStatus = "verified",
                VerificationReasonCode = "source-log",
            },
            new ModifierProvenanceEntity
            {
                DerivedGymTrainLogId = "train-b",
                Scope = "personal",
                SubjectId = "u1",
                ValidFromUtc = boundary,
                ValidToUtc = DateTimeOffset.Parse("2026-01-01T02:00:00Z"),
                VerificationStatus = "unresolved",
                VerificationReasonCode = "missing-faction",
            });

        await db.SaveChangesAsync();

        var rows = await db.ModifierProvenance
            .Where(p => p.Scope == "personal")
            .OrderBy(p => p.ValidFromUtc)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].ValidToUtc, rows[1].ValidFromUtc);
    }
}
