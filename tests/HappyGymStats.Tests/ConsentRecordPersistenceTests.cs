using HappyGymStats.Contracts.Compliance;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ConsentRecordPersistenceTests
{
    [Fact]
    public async Task Consent_record_persists_published_version_purpose_and_revocation_without_player_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var anonymousId = Guid.NewGuid();
        var acceptedAt = new DateTimeOffset(2026, 9, 4, 16, 0, 0, TimeSpan.Zero);
        var revokedAt = acceptedAt.AddHours(2);

        db.ConsentRecords.Add(new ConsentRecordEntity
        {
            AnonymousId = anonymousId,
            DocumentVersion = TermsDocument.Version,
            Purpose = ConsentPurposes.WarMemberApiKey,
            AcceptedAtUtc = acceptedAt,
            RevokedAtUtc = revokedAt
        });

        await db.SaveChangesAsync();

        var stored = await db.ConsentRecords.SingleAsync();
        Assert.True(stored.Id > 0);
        Assert.Equal(anonymousId, stored.AnonymousId);
        Assert.Equal(TermsDocument.Version, stored.DocumentVersion);
        Assert.Equal(ConsentPurposes.WarMemberApiKey, stored.Purpose);
        Assert.Equal(acceptedAt, stored.AcceptedAtUtc);
        Assert.Equal(revokedAt, stored.RevokedAtUtc);

        var properties = typeof(ConsentRecordEntity).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(properties, name => name.Contains("Torn", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Consent_records_keep_distinct_versions_as_auditable_history()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var anonymousId = Guid.NewGuid();
        db.ConsentRecords.AddRange(
            new ConsentRecordEntity
            {
                AnonymousId = anonymousId,
                DocumentVersion = "1.0.0",
                Purpose = ConsentPurposes.WarMemberApiKey,
                AcceptedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
                RevokedAtUtc = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero)
            },
            new ConsentRecordEntity
            {
                AnonymousId = anonymousId,
                DocumentVersion = TermsDocument.Version,
                Purpose = ConsentPurposes.WarMemberApiKey,
                AcceptedAtUtc = new DateTimeOffset(2026, 9, 4, 9, 1, 0, TimeSpan.Zero)
            });

        await db.SaveChangesAsync();

        var rows = await db.ConsentRecords
            .Where(record => record.AnonymousId == anonymousId)
            .OrderBy(record => record.AcceptedAtUtc)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("1.0.0", rows[0].DocumentVersion);
        Assert.NotNull(rows[0].RevokedAtUtc);
        Assert.Equal(TermsDocument.Version, rows[1].DocumentVersion);
        Assert.Null(rows[1].RevokedAtUtc);
    }
}
