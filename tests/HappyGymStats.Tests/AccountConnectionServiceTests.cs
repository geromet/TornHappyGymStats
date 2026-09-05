using System.Net;
using System.Text;
using System.Text.Json;
using HappyGymStats.Api.Services;
using HappyGymStats.Contracts.Compliance;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class AccountConnectionServiceTests
{
    private const string FixtureKey = "member-fixture-secret";

    [Fact]
    public async Task Connect_validates_with_authorization_header_and_persists_current_consent_encrypted()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = Guid.NewGuid();
        fixture.Db.IdentityMap.Add(new IdentityMapEntity { AnonymousId = owner, CreatedAtUtc = fixture.Now.AddDays(-1) });
        await fixture.Db.SaveChangesAsync();

        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/v2/user/basic?selections=basic", request.RequestUri?.AbsoluteUri);
            Assert.Equal("ApiKey", request.Headers.Authorization?.Scheme);
            Assert.Equal(FixtureKey, request.Headers.Authorization?.Parameter);
            Assert.DoesNotContain(FixtureKey, request.RequestUri?.AbsoluteUri ?? string.Empty, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse("""{"player_id":10101}"""));
        });
        var validator = new TornConnectionValidator(new HttpClient(handler) { BaseAddress = new Uri("https://api.torn.com/") }, new TornRateLimiter());
        var vault = new WarKeyVault(Enumerable.Repeat((byte)0x31, 32).ToArray());
        var store = new StoredApiKeyStore(fixture.Db, vault, fixture.Clock);
        var sut = new AccountConnectionService(store, validator);

        var result = await sut.ConnectAsync(owner, FixtureKey, consentAccepted: true);

        Assert.Equal(AccountConnectionOperationStatus.Success, result.Status);
        Assert.NotNull(result.Connection);
        Assert.True(result.Connection!.Connected);
        Assert.Equal(10101, result.Connection.TornPlayerId);
        Assert.Equal(TermsDocument.Version, result.Connection.Consent?.DocumentVersion);
        Assert.Equal(ConsentPurposes.WarMemberApiKey, result.Connection.Consent?.Purpose);

        var consent = await fixture.Db.ConsentRecords.AsNoTracking().SingleAsync();
        var stored = await fixture.Db.StoredApiKeys.AsNoTracking().SingleAsync();
        Assert.Equal(owner, consent.AnonymousId);
        Assert.Equal(consent.Id, stored.ConsentRecordId);
        Assert.Equal(FixtureKey, vault.UseKey(stored.Ciphertext, stored.TornPlayerId, ConsentPurposes.WarMemberApiKey, key => key));

        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(FixtureKey, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("ciphertext", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connect_without_explicit_consent_never_calls_torn_or_writes_state()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = Guid.NewGuid();
        fixture.Db.IdentityMap.Add(new IdentityMapEntity { AnonymousId = owner, CreatedAtUtc = fixture.Now.AddDays(-1) });
        await fixture.Db.SaveChangesAsync();

        var validatorCalled = false;
        var validator = new DelegateValidator((_, _) => { validatorCalled = true; return Task.FromResult(10101); });
        var store = new StoredApiKeyStore(fixture.Db, new WarKeyVault(Enumerable.Repeat((byte)0x32, 32).ToArray()), fixture.Clock);
        var sut = new AccountConnectionService(store, validator);

        var result = await sut.ConnectAsync(owner, FixtureKey, consentAccepted: false);

        Assert.Equal(AccountConnectionOperationStatus.ConsentRequired, result.Status);
        Assert.False(validatorCalled);
        Assert.Empty(await fixture.Db.ConsentRecords.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.StoredApiKeys.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Torn_rejection_is_safe_and_does_not_persist_or_echo_submitted_key()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = Guid.NewGuid();
        fixture.Db.IdentityMap.Add(new IdentityMapEntity { AnonymousId = owner, CreatedAtUtc = fixture.Now.AddDays(-1) });
        await fixture.Db.SaveChangesAsync();

        var handler = new RecordingHandler((request, _) =>
        {
            Assert.DoesNotContain(FixtureKey, request.RequestUri?.AbsoluteUri ?? string.Empty, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse("""{"error":{"code":2,"error":"Incorrect key member-fixture-secret"}}"""));
        });
        var validator = new TornConnectionValidator(new HttpClient(handler) { BaseAddress = new Uri("https://api.torn.com/") }, new TornRateLimiter());
        var store = new StoredApiKeyStore(fixture.Db, new WarKeyVault(Enumerable.Repeat((byte)0x33, 32).ToArray()), fixture.Clock);
        var sut = new AccountConnectionService(store, validator);

        var result = await sut.ConnectAsync(owner, FixtureKey, consentAccepted: true);

        Assert.Equal(AccountConnectionOperationStatus.InvalidTornApiKey, result.Status);
        Assert.DoesNotContain(FixtureKey, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Empty(await fixture.Db.ConsentRecords.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.StoredApiKeys.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Vault_unavailable_leaves_no_consent_or_key_and_returns_safe_status()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = Guid.NewGuid();
        fixture.Db.IdentityMap.Add(new IdentityMapEntity { AnonymousId = owner, CreatedAtUtc = fixture.Now.AddDays(-1) });
        await fixture.Db.SaveChangesAsync();

        var store = new StoredApiKeyStore(fixture.Db, () => throw new WarKeyVaultConfigurationException("fixture master secret is unavailable"), fixture.Clock);
        var sut = new AccountConnectionService(store, new DelegateValidator((_, _) => Task.FromResult(10101)));

        var result = await sut.ConnectAsync(owner, FixtureKey, consentAccepted: true);

        Assert.Equal(AccountConnectionOperationStatus.KeyVaultUnavailable, result.Status);
        Assert.DoesNotContain("fixture master secret", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Empty(await fixture.Db.ConsentRecords.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.StoredApiKeys.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Revoke_removes_only_callers_key_and_revokes_all_callers_key_consents()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        fixture.Db.IdentityMap.AddRange(
            new IdentityMapEntity { AnonymousId = alice, CreatedAtUtc = fixture.Now.AddDays(-1) },
            new IdentityMapEntity { AnonymousId = bob, CreatedAtUtc = fixture.Now.AddDays(-1) });
        await fixture.Db.SaveChangesAsync();

        var vault = new WarKeyVault(Enumerable.Repeat((byte)0x34, 32).ToArray());
        var store = new StoredApiKeyStore(fixture.Db, vault, fixture.Clock);
        await store.StoreWithConsentAsync(alice, 10101, "alice-key");
        await store.StoreWithConsentAsync(bob, 20202, "bob-key");
        fixture.Db.ConsentRecords.Add(new ConsentRecordEntity { AnonymousId = alice, DocumentVersion = "1.0.0", Purpose = ConsentPurposes.WarMemberApiKey, AcceptedAtUtc = fixture.Now.AddDays(-2) });
        await fixture.Db.SaveChangesAsync();

        var result = await store.RevokeAsync(alice);

        Assert.Equal(StoredApiKeyRevokeStatus.Revoked, result);
        Assert.False(await fixture.Db.StoredApiKeys.AsNoTracking().AnyAsync(x => x.AnonymousId == alice));
        Assert.True(await fixture.Db.StoredApiKeys.AsNoTracking().AnyAsync(x => x.AnonymousId == bob));
        Assert.False(await fixture.Db.ConsentRecords.AsNoTracking().AnyAsync(x => x.AnonymousId == alice && x.Purpose == ConsentPurposes.WarMemberApiKey && x.RevokedAtUtc == null));
        Assert.True(await fixture.Db.ConsentRecords.AsNoTracking().AnyAsync(x => x.AnonymousId == bob && x.Purpose == ConsentPurposes.WarMemberApiKey && x.RevokedAtUtc == null));
    }

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class DelegateValidator(Func<string, CancellationToken, Task<int>> validate) : ITornConnectionValidator
    {
        public Task<int> GetPlayerIdAsync(string apiKey, CancellationToken cancellationToken = default) => validate(apiKey, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(SqliteConnection connection, HappyGymStatsDbContext db, DateTimeOffset now)
        {
            Connection = connection;
            Db = db;
            Now = now;
            Clock = new FixedTimeProvider(now);
        }

        public SqliteConnection Connection { get; }
        public HappyGymStatsDbContext Db { get; }
        public DateTimeOffset Now { get; }
        public FixedTimeProvider Clock { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>().UseSqlite(connection).Options;
            var db = new HappyGymStatsDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, db, new DateTimeOffset(2026, 9, 5, 12, 30, 0, TimeSpan.Zero));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
