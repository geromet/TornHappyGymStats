using DotNet.Testcontainers.Builders;
using HappyGymStats.Contracts.Compliance;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace HappyGymStats.Tests;

public sealed class AccountConnectionPostgresTests : IAsyncLifetime
{
    private const string SkipEnvVar = "HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION";
    private const string RequireEnvVar = "HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION";
    private readonly ITestOutputHelper _output;
    private PostgreSqlContainer? _postgres;
    private string? _skipReason;

    public AccountConnectionPostgresTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        if (!IsRequired() && IsTruthy(Environment.GetEnvironmentVariable(SkipEnvVar)))
        {
            _skipReason = $"{SkipEnvVar} is set; account-connection PostgreSQL proof intentionally skipped.";
            return;
        }

        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("happygymstats")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
            await _postgres.StartAsync(startupCts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException
                                   or ArgumentException or InvalidOperationException or DockerUnavailableException)
        {
            _skipReason = $"PostgreSQL account-connection proof could not start Docker/Testcontainers: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact(DisplayName = "PostgresApiIntegration: account connections replace and revoke owner-scoped")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Replace_and_revoke_remain_owner_scoped_on_postgres()
    {
        if (_skipReason is not null)
        {
            if (IsRequired())
            {
                Assert.True(false, $"{RequireEnvVar} is set, so this tier must run: {_skipReason}");
            }

            _output.WriteLine(_skipReason);
            return;
        }

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString())
            .Options;
        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 9, 5, 12, 45, 0, TimeSpan.Zero);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        db.IdentityMap.AddRange(
            new IdentityMapEntity { AnonymousId = alice, CreatedAtUtc = now.AddDays(-1) },
            new IdentityMapEntity { AnonymousId = bob, CreatedAtUtc = now.AddDays(-1) });
        await db.SaveChangesAsync();

        var vault = new WarKeyVault(Enumerable.Repeat((byte)0x45, 32).ToArray());
        var store = new StoredApiKeyStore(db, vault, new FixedTimeProvider(now));
        Assert.Equal(StoredApiKeyWriteStatus.Stored, await store.StoreWithConsentAsync(alice, 10101, "alice-first-key"));
        Assert.Equal(StoredApiKeyWriteStatus.Stored, await store.StoreWithConsentAsync(alice, 10101, "alice-replacement-key"));
        Assert.Equal(StoredApiKeyWriteStatus.Stored, await store.StoreWithConsentAsync(bob, 20202, "bob-key"));

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.StoredApiKeys.AsNoTracking().CountAsync());
        var aliceRow = await db.StoredApiKeys.AsNoTracking().SingleAsync(x => x.AnonymousId == alice);
        Assert.Equal("alice-replacement-key", vault.UseKey(aliceRow.Ciphertext, aliceRow.TornPlayerId, ConsentPurposes.WarMemberApiKey, key => key));

        Assert.Equal(StoredApiKeyRevokeStatus.Revoked, await store.RevokeAsync(alice));
        db.ChangeTracker.Clear();

        Assert.False(await db.StoredApiKeys.AsNoTracking().AnyAsync(x => x.AnonymousId == alice));
        var bobRow = await db.StoredApiKeys.AsNoTracking().SingleAsync(x => x.AnonymousId == bob);
        Assert.Equal(20202, bobRow.TornPlayerId);
        Assert.Equal("bob-key", vault.UseKey(bobRow.Ciphertext, bobRow.TornPlayerId, ConsentPurposes.WarMemberApiKey, key => key));
        Assert.False(await db.ConsentRecords.AsNoTracking().AnyAsync(x => x.AnonymousId == alice && x.Purpose == ConsentPurposes.WarMemberApiKey && x.RevokedAtUtc == null));
        Assert.True(await db.ConsentRecords.AsNoTracking().AnyAsync(x => x.AnonymousId == bob && x.Purpose == ConsentPurposes.WarMemberApiKey && x.DocumentVersion == TermsDocument.Version && x.RevokedAtUtc == null));

        Assert.Equal(StoredApiKeyConnectionStatus.NotConnected, (await store.GetConnectionStateAsync(alice)).Status);
        Assert.Equal(StoredApiKeyConnectionStatus.Connected, (await store.GetConnectionStateAsync(bob)).Status);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static bool IsRequired() => IsTruthy(Environment.GetEnvironmentVariable(RequireEnvVar));
    private static bool IsTruthy(string? raw) => string.Equals(raw, "1", StringComparison.Ordinal) || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
}
