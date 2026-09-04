using HappyGymStats.Contracts.Compliance;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class StoredApiKeyPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private const string TestApiKey = "fixture-member-key-do-not-log";

    [Fact]
    public async Task Current_consent_and_owner_store_only_vault_ciphertext()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var anonymousId = Guid.NewGuid();
        await fixture.AddOwnerAndConsentAsync(anonymousId);
        var vault = new WarKeyVault(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var store = new StoredApiKeyStore(fixture.Db, vault, new FixedTimeProvider(Now));

        var status = await store.StoreAsync(anonymousId, 1234567, TestApiKey);

        Assert.Equal(StoredApiKeyWriteStatus.Stored, status);
        var row = await fixture.Db.StoredApiKeys.AsNoTracking().SingleAsync();
        Assert.Equal(anonymousId, row.AnonymousId);
        Assert.Equal(1234567, row.TornPlayerId);
        Assert.Equal(Now, row.StoredAtUtc);
        Assert.NotEmpty(row.Ciphertext);
        Assert.DoesNotContain(TestApiKey, Convert.ToBase64String(row.Ciphertext), StringComparison.Ordinal);
        Assert.Equal(TestApiKey, vault.UseKey(
            row.Ciphertext,
            row.TornPlayerId,
            ConsentPurposes.WarMemberApiKey,
            key => key));
        Assert.DoesNotContain(
            typeof(StoredApiKeyEntity).GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    [Fact]
    public async Task Another_tenants_consent_cannot_authorize_storage()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await fixture.AddOwnerAsync(alice);
        await fixture.AddOwnerAsync(bob);
        await fixture.AddConsentAsync(alice);
        var store = fixture.CreateStore();

        var status = await store.StoreAsync(bob, 222, TestApiKey);

        Assert.Equal(StoredApiKeyWriteStatus.ConsentRequired, status);
        Assert.False(await fixture.Db.StoredApiKeys.AnyAsync());
    }

    [Fact]
    public async Task Revoked_or_stale_consent_cannot_authorize_storage()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var revoked = Guid.NewGuid();
        var stale = Guid.NewGuid();
        await fixture.AddOwnerAsync(revoked);
        await fixture.AddOwnerAsync(stale);
        await fixture.AddConsentAsync(revoked, revokedAtUtc: Now.AddMinutes(-1));
        await fixture.AddConsentAsync(stale, documentVersion: "1.0.0");
        var store = fixture.CreateStore();

        Assert.Equal(StoredApiKeyWriteStatus.ConsentRequired,
            await store.StoreAsync(revoked, 333, TestApiKey));
        Assert.Equal(StoredApiKeyWriteStatus.ConsentRequired,
            await store.StoreAsync(stale, 444, TestApiKey));
        Assert.False(await fixture.Db.StoredApiKeys.AnyAsync());
    }

    [Fact]
    public async Task Consent_without_an_owning_identity_cannot_authorize_storage()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var anonymousId = Guid.NewGuid();
        await fixture.AddConsentAsync(anonymousId);
        var store = fixture.CreateStore();

        var status = await store.StoreAsync(anonymousId, 555, TestApiKey);

        Assert.Equal(StoredApiKeyWriteStatus.OwnerNotFound, status);
        Assert.False(await fixture.Db.StoredApiKeys.AnyAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public HappyGymStatsDbContext Db { get; }

        private SqliteFixture(SqliteConnection connection, HappyGymStatsDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new HappyGymStatsDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, db);
        }

        public StoredApiKeyStore CreateStore()
            => new(
                Db,
                new WarKeyVault(Enumerable.Repeat((byte)0x5a, 32).ToArray()),
                new FixedTimeProvider(Now));

        public async Task AddOwnerAndConsentAsync(Guid anonymousId)
        {
            await AddOwnerAsync(anonymousId);
            await AddConsentAsync(anonymousId);
        }

        public async Task AddOwnerAsync(Guid anonymousId)
        {
            Db.IdentityMap.Add(new IdentityMapEntity
            {
                AnonymousId = anonymousId,
                CreatedAtUtc = Now.AddDays(-1),
            });
            await Db.SaveChangesAsync();
        }

        public async Task AddConsentAsync(
            Guid anonymousId,
            string documentVersion = TermsDocument.Version,
            DateTimeOffset? revokedAtUtc = null)
        {
            Db.ConsentRecords.Add(new ConsentRecordEntity
            {
                AnonymousId = anonymousId,
                DocumentVersion = documentVersion,
                Purpose = ConsentPurposes.WarMemberApiKey,
                AcceptedAtUtc = Now.AddMinutes(-10),
                RevokedAtUtc = revokedAtUtc,
            });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
