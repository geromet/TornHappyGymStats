using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HappyGymStats.Tests;

public sealed class WarTargetCoordinationPostgresPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-06T10:00:00Z");
    private PostgreSqlContainer? _postgres;
    private bool _available;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("happygymstats")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();

            await using var db = CreateDbContext();
            await db.Database.MigrateAsync();
            _available = true;
        }
        catch when (!IsRequired())
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Claim_and_ordered_reservation_survive_context_restart()
    {
        if (!_available) return;

        var claim = Claim(attacker: 11, target: 21);
        var reservation = new WarTargetReservation(
            Guid.NewGuid(), 100, 200, 22,
            Now.AddMinutes(1), Now.AddMinutes(20), 12, [13, 14]);

        await using (var writeDb = CreateDbContext())
        {
            var repository = new WarTargetCoordinationRepository(writeDb);
            Assert.True(await repository.TryCreateClaimAsync(claim, CancellationToken.None));
            await repository.SaveReservationAsync(reservation, CancellationToken.None);
        }

        await using var readDb = CreateDbContext();
        var readRepository = new WarTargetCoordinationRepository(readDb);
        var persistedClaims = await readRepository.GetClaimsAsync(100, 200, CancellationToken.None);
        var persistedClaim = Assert.Single(persistedClaims);
        Assert.Equal(claim.Id, persistedClaim.Id);
        Assert.Equal(claim.TargetMemberId, persistedClaim.TargetMemberId);
        Assert.Equal(claim.AttackerMemberId, persistedClaim.AttackerMemberId);
        Assert.Equal(claim.ClaimedAtUtc, persistedClaim.ClaimedAtUtc);
        Assert.Equal(claim.ExpiresAtUtc, persistedClaim.ExpiresAtUtc);

        var persistedReservation = await readRepository.GetReservationAsync(reservation.Id, CancellationToken.None);
        Assert.NotNull(persistedReservation);
        Assert.Equal(new long[] { 12, 13, 14 }, persistedReservation.CandidateOrder);
        Assert.Equal(reservation.ActivatesAtUtc, persistedReservation.ActivatesAtUtc);
        Assert.Equal(reservation.ExpiresAtUtc, persistedReservation.ExpiresAtUtc);
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Concurrent_primary_claims_for_same_target_serialize_to_one_winner()
    {
        if (!_available) return;

        await using var leftDb = CreateDbContext();
        await using var rightDb = CreateDbContext();
        var left = new WarTargetCoordinationRepository(leftDb);
        var right = new WarTargetCoordinationRepository(rightDb);

        var results = await Task.WhenAll(
            left.TryCreateClaimAsync(Claim(11, 21), CancellationToken.None),
            right.TryCreateClaimAsync(Claim(12, 21), CancellationToken.None));

        Assert.Single(results.Where(result => result));

        await using var verifyDb = CreateDbContext();
        var claims = await new WarTargetCoordinationRepository(verifyDb)
            .GetClaimsAsync(100, 200, CancellationToken.None);
        Assert.Single(claims.Where(claim => claim.Mode == WarTargetClaimMode.Primary && claim.IsActiveAt(Now)));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Concurrent_primary_claims_for_same_attacker_serialize_to_one_winner()
    {
        if (!_available) return;

        await using var leftDb = CreateDbContext();
        await using var rightDb = CreateDbContext();
        var left = new WarTargetCoordinationRepository(leftDb);
        var right = new WarTargetCoordinationRepository(rightDb);

        var results = await Task.WhenAll(
            left.TryCreateClaimAsync(Claim(11, 21), CancellationToken.None),
            right.TryCreateClaimAsync(Claim(11, 22), CancellationToken.None));

        Assert.Single(results.Where(result => result));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Overlapping_future_primary_windows_are_rejected_before_they_become_live()
    {
        if (!_available) return;

        await using var db = CreateDbContext();
        var repository = new WarTargetCoordinationRepository(db);
        var first = new WarTargetClaim(
            Guid.NewGuid(), 100, 200, 21, 11,
            Now.AddMinutes(10), Now.AddMinutes(20));
        var overlap = new WarTargetClaim(
            Guid.NewGuid(), 100, 200, 21, 12,
            Now.AddMinutes(15), Now.AddMinutes(25));
        var adjacent = new WarTargetClaim(
            Guid.NewGuid(), 100, 200, 21, 13,
            Now.AddMinutes(20), Now.AddMinutes(30));

        Assert.True(await repository.TryCreateClaimAsync(first, CancellationToken.None));
        Assert.False(await repository.TryCreateClaimAsync(overlap, CancellationToken.None));
        Assert.True(await repository.TryCreateClaimAsync(adjacent, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Expired_primary_and_assist_do_not_consume_nonoverlapping_primary_slot()
    {
        if (!_available) return;

        await using var db = CreateDbContext();
        var repository = new WarTargetCoordinationRepository(db);
        var expired = new WarTargetClaim(
            Guid.NewGuid(), 100, 200, 21, 11,
            Now.AddMinutes(-10), Now, WarTargetClaimMode.Primary);
        var assist = new WarTargetClaim(
            Guid.NewGuid(), 100, 200, 21, 12,
            Now.AddMinutes(-1), Now.AddMinutes(5), WarTargetClaimMode.Assist);
        var replacement = Claim(13, 21);

        Assert.True(await repository.TryCreateClaimAsync(expired, CancellationToken.None));
        Assert.True(await repository.TryCreateClaimAsync(assist, CancellationToken.None));
        Assert.True(await repository.TryCreateClaimAsync(replacement, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Conflict_scope_isolated_by_faction_and_war()
    {
        if (!_available) return;

        await using var db = CreateDbContext();
        var repository = new WarTargetCoordinationRepository(db);
        Assert.True(await repository.TryCreateClaimAsync(Claim(11, 21), CancellationToken.None));
        Assert.True(await repository.TryCreateClaimAsync(
            new WarTargetClaim(Guid.NewGuid(), 100, 201, 21, 11, Now, Now.AddMinutes(5)),
            CancellationToken.None));
        Assert.True(await repository.TryCreateClaimAsync(
            new WarTargetClaim(Guid.NewGuid(), 101, 200, 21, 11, Now, Now.AddMinutes(5)),
            CancellationToken.None));
    }

    private static WarTargetClaim Claim(long attacker, long target) =>
        new(Guid.NewGuid(), 100, 200, target, attacker, Now, Now.AddMinutes(5));

    private HappyGymStatsDbContext CreateDbContext()
    {
        if (_postgres is null)
            throw new InvalidOperationException("Postgres container was not initialized.");

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new HappyGymStatsDbContext(options);
    }

    private static bool IsRequired()
    {
        var raw = Environment.GetEnvironmentVariable("HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION");
        return string.Equals(raw, "1", StringComparison.Ordinal)
               || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
