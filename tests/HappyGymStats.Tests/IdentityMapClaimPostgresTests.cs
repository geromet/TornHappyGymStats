using DotNet.Testcontainers.Builders;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace HappyGymStats.Tests;

public sealed class IdentityMapClaimPostgresTests : IAsyncLifetime
{
    private const string SkipEnvVar = "HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION";
    private const string RequireEnvVar = "HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION";
    private readonly ITestOutputHelper _output;
    private PostgreSqlContainer? _postgres;
    private string? _skipReason;

    public IdentityMapClaimPostgresTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        if (!IsRequired() && IsTruthy(Environment.GetEnvironmentVariable(SkipEnvVar)))
        {
            _skipReason = $"{SkipEnvVar} is set; identity-claim PostgreSQL proof intentionally skipped.";
            return;
        }

        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
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
            _skipReason = $"PostgreSQL identity-claim proof could not start Docker/Testcontainers: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact(DisplayName = "PostgresApiIntegration: provisional identity claim is single-use, subject-unique, and expiry-aware")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Provisional_claim_is_atomic_subject_unique_and_expiry_aware()
    {
        if (_skipReason is not null)
        {
            if (IsRequired())
                Assert.True(false, $"{RequireEnvVar} is set, so this tier must run: {_skipReason}");

            _output.WriteLine(_skipReason);
            return;
        }

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString())
            .Options;
        var now = new DateTimeOffset(2030, 6, 15, 12, 30, 45, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);

        await using (var setup = new HappyGymStatsDbContext(options))
        {
            await setup.Database.MigrateAsync();

            setup.IdentityMap.AddRange(
                NewProvisional(Guid.Parse("11111111-1111-1111-1111-111111111111"), now.AddHours(1)),
                NewProvisional(Guid.Parse("22222222-2222-2222-2222-222222222222"), now.AddHours(1)),
                NewProvisional(Guid.Parse("33333333-3333-3333-3333-333333333333"), now.AddHours(1)),
                NewProvisional(Guid.Parse("44444444-4444-4444-4444-444444444444"), now.AddMinutes(-1)),
                NewProvisional(Guid.Parse("55555555-5555-5555-5555-555555555555"), now));
            await setup.SaveChangesAsync();
        }

        var singleUseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using var singleUseA = new HappyGymStatsDbContext(options);
        await using var singleUseB = new HappyGymStatsDbContext(options);
        var singleUseResults = await Task.WhenAll(
            new IdentityMapRepository(singleUseA, clock).ClaimProvisionalAsync(singleUseId, "subject-a", CancellationToken.None),
            new IdentityMapRepository(singleUseB, clock).ClaimProvisionalAsync(singleUseId, "subject-b", CancellationToken.None));

        Assert.Single(singleUseResults.Where(result => result));

        var subjectUniqueA = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var subjectUniqueB = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await using var subjectContextA = new HappyGymStatsDbContext(options);
        await using var subjectContextB = new HappyGymStatsDbContext(options);
        var subjectResults = await Task.WhenAll(
            new IdentityMapRepository(subjectContextA, clock).ClaimProvisionalAsync(subjectUniqueA, "shared-subject", CancellationToken.None),
            new IdentityMapRepository(subjectContextB, clock).ClaimProvisionalAsync(subjectUniqueB, "shared-subject", CancellationToken.None));

        Assert.Single(subjectResults.Where(result => result));

        var expiredId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        await using var expiredContext = new HappyGymStatsDbContext(options);
        Assert.False(await new IdentityMapRepository(expiredContext, clock)
            .ClaimProvisionalAsync(expiredId, "expired-subject", CancellationToken.None));

        var boundaryId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        await using var boundaryContext = new HappyGymStatsDbContext(options);
        Assert.False(await new IdentityMapRepository(boundaryContext, clock)
            .ClaimProvisionalAsync(boundaryId, "boundary-subject", CancellationToken.None));

        await using var verify = new HappyGymStatsDbContext(options);
        var singleUse = await verify.IdentityMap.AsNoTracking().SingleAsync(x => x.AnonymousId == singleUseId);
        Assert.False(singleUse.IsProvisional);
        Assert.Null(singleUse.ExpiresAtUtc);
        Assert.Contains(singleUse.KeycloakSub, new[] { "subject-a", "subject-b" });

        var subjectRows = await verify.IdentityMap.AsNoTracking()
            .Where(x => x.AnonymousId == subjectUniqueA || x.AnonymousId == subjectUniqueB)
            .ToListAsync();
        Assert.Single(subjectRows.Where(x => x.KeycloakSub == "shared-subject"));
        Assert.Single(subjectRows.Where(x => x.IsProvisional));

        var expired = await verify.IdentityMap.AsNoTracking().SingleAsync(x => x.AnonymousId == expiredId);
        Assert.True(expired.IsProvisional);
        Assert.Null(expired.KeycloakSub);
        Assert.NotNull(expired.ExpiresAtUtc);
    }

    private static IdentityMapEntity NewProvisional(Guid anonymousId, DateTimeOffset expiresAtUtc)
        => new()
        {
            AnonymousId = anonymousId,
            IsProvisional = true,
            CreatedAtUtc = expiresAtUtc.AddHours(-1),
            ExpiresAtUtc = expiresAtUtc,
        };

    private static bool IsRequired() => IsTruthy(Environment.GetEnvironmentVariable(RequireEnvVar));

    private static bool IsTruthy(string? raw)
        => string.Equals(raw, "1", StringComparison.Ordinal)
           || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
