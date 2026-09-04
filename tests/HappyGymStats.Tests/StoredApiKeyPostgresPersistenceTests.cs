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

public sealed class StoredApiKeyPostgresPersistenceTests : IAsyncLifetime
{
    private const string SkipEnvVar = "HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION";
    private const string RequireEnvVar = "HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION";
    private readonly ITestOutputHelper _output;
    private PostgreSqlContainer? _postgres;
    private string? _skipReason;

    public StoredApiKeyPostgresPersistenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!IsRequired() && IsTruthy(Environment.GetEnvironmentVariable(SkipEnvVar)))
        {
            _skipReason = $"{SkipEnvVar} is set; stored-key PostgreSQL persistence proof intentionally skipped.";
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
            _skipReason = $"PostgreSQL stored-key proof could not start Docker/Testcontainers: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact(DisplayName = "PostgresApiIntegration: stored key requires same-tenant current consent")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Stored_key_migration_and_transactional_tenant_gate_hold_on_postgres()
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

        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        db.IdentityMap.AddRange(
            new IdentityMapEntity { AnonymousId = alice, CreatedAtUtc = now.AddDays(-1) },
            new IdentityMapEntity { AnonymousId = bob, CreatedAtUtc = now.AddDays(-1) });
        db.ConsentRecords.Add(new ConsentRecordEntity
        {
            AnonymousId = alice,
            DocumentVersion = TermsDocument.Version,
            Purpose = ConsentPurposes.WarMemberApiKey,
            AcceptedAtUtc = now.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        var vault = new WarKeyVault(Enumerable.Repeat((byte)0x2d, 32).ToArray());
        var store = new StoredApiKeyStore(db, vault, new FixedTimeProvider(now));
        Assert.Equal(StoredApiKeyWriteStatus.Stored,
            await store.StoreAsync(alice, 10101, "postgres-fixture-key"));
        Assert.Equal(StoredApiKeyWriteStatus.ConsentRequired,
            await store.StoreAsync(bob, 20202, "must-not-be-stored"));

        var row = await db.StoredApiKeys.AsNoTracking().SingleAsync();
        Assert.Equal(alice, row.AnonymousId);
        Assert.Equal(10101, row.TornPlayerId);
        Assert.Equal(TermsDocument.Version,
            await db.ConsentRecords.Where(x => x.Id == row.ConsentRecordId).Select(x => x.DocumentVersion).SingleAsync());
        Assert.Equal("postgres-fixture-key", vault.UseKey(
            row.Ciphertext,
            row.TornPlayerId,
            ConsentPurposes.WarMemberApiKey,
            key => key));

        var tableExists = await db.Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'StoredApiKeys') AS \"Value\"")
            .SingleAsync();
        Assert.True(tableExists);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static bool IsRequired() => IsTruthy(Environment.GetEnvironmentVariable(RequireEnvVar));

    private static bool IsTruthy(string? raw) =>
        string.Equals(raw, "1", StringComparison.Ordinal)
        || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
}
