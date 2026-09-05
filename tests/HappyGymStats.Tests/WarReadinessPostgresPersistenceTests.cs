using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HappyGymStats.Tests;

public sealed class WarReadinessPostgresPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
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
    public async Task Current_declaration_round_trips_and_stale_writer_cannot_overwrite_newer_revision()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();
        var repository = new WarReadinessRepository(db);
        var first = WarReadinessMutationPolicy.Set(null, Command(42, WarReadinessState.Ready));

        await repository.SaveAsync(first, expectedRevision: 0, CancellationToken.None);
        var restoredFirst = await repository.GetAsync(100, 200, 42, CancellationToken.None);
        AssertDeclaration(first, Assert.IsType<WarReadinessDeclaration>(restoredFirst));

        var updated = WarReadinessMutationPolicy.Set(
            first,
            Command(42, WarReadinessState.Limited) with
            {
                Note = "late shift",
                UpdatedAtUtc = Now.AddMinutes(10),
            });
        await repository.SaveAsync(updated, expectedRevision: first.Revision, CancellationToken.None);

        var staleAlternative = WarReadinessMutationPolicy.Set(
            first,
            Command(42, WarReadinessState.Unavailable) with { UpdatedAtUtc = Now.AddMinutes(5) });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(staleAlternative, expectedRevision: first.Revision, CancellationToken.None));

        var restoredUpdated = await repository.GetAsync(100, 200, 42, CancellationToken.None);
        AssertDeclaration(updated, Assert.IsType<WarReadinessDeclaration>(restoredUpdated));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task War_reads_are_faction_scoped_and_cannot_extract_adjacent_scope()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();
        var repository = new WarReadinessRepository(db);
        var expected = WarReadinessMutationPolicy.Set(null, Command(10));
        var sameWar = WarReadinessMutationPolicy.Set(null, Command(20, factionId: 100, warId: 200));
        var otherFaction = WarReadinessMutationPolicy.Set(null, Command(30, factionId: 101, warId: 200));
        var otherWar = WarReadinessMutationPolicy.Set(null, Command(40, factionId: 100, warId: 201));

        await repository.SaveAsync(expected, 0, CancellationToken.None);
        await repository.SaveAsync(sameWar, 0, CancellationToken.None);
        await repository.SaveAsync(otherFaction, 0, CancellationToken.None);
        await repository.SaveAsync(otherWar, 0, CancellationToken.None);

        var scoped = await repository.GetForWarAsync(100, 200, CancellationToken.None);

        Assert.Equal(new long[] { 10, 20 }, scoped.Select(item => item.MemberId));
        Assert.All(scoped, item =>
        {
            Assert.Equal(100, item.FactionId);
            Assert.Equal(200, item.WarId);
        });
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Clear_requires_exact_current_revision()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();
        var repository = new WarReadinessRepository(db);
        var first = WarReadinessMutationPolicy.Set(null, Command(52));
        await repository.SaveAsync(first, 0, CancellationToken.None);

        var second = WarReadinessMutationPolicy.Set(
            first,
            Command(52, WarReadinessState.Limited) with { UpdatedAtUtc = Now.AddMinutes(2) });
        await repository.SaveAsync(second, first.Revision, CancellationToken.None);

        Assert.False(await repository.ClearAsync(100, 200, 52, first.Revision, CancellationToken.None));
        Assert.NotNull(await repository.GetAsync(100, 200, 52, CancellationToken.None));

        Assert.True(await repository.ClearAsync(100, 200, 52, second.Revision, CancellationToken.None));
        Assert.Null(await repository.GetAsync(100, 200, 52, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Database_constraints_reject_invalid_state_window_and_revision()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "WarReadinessDeclarations"
                ("FactionId", "WarId", "MemberId", "State", "WindowStartUtc", "WindowEndUtc", "Note", "UpdatedAtUtc", "Revision")
            VALUES
                (700, 800, 900, 999, TIMESTAMPTZ '2026-09-05 12:00:00+00', TIMESTAMPTZ '2026-09-05 13:00:00+00', NULL, TIMESTAMPTZ '2026-09-05 12:00:00+00', 1)
            """));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "WarReadinessDeclarations"
                ("FactionId", "WarId", "MemberId", "State", "WindowStartUtc", "WindowEndUtc", "Note", "UpdatedAtUtc", "Revision")
            VALUES
                (701, 801, 901, 1, TIMESTAMPTZ '2026-09-05 14:00:00+00', TIMESTAMPTZ '2026-09-05 13:00:00+00', NULL, TIMESTAMPTZ '2026-09-05 12:00:00+00', 0)
            """));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Repository_rejects_revision_mismatch_before_write()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();
        var repository = new WarReadinessRepository(db);
        var declaration = WarReadinessMutationPolicy.Set(null, Command(62));

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.SaveAsync(declaration, expectedRevision: 1, CancellationToken.None));
        Assert.Null(await repository.GetAsync(100, 200, 62, CancellationToken.None));
    }

    private static SetWarReadinessCommand Command(
        long memberId,
        WarReadinessState state = WarReadinessState.Ready,
        long factionId = 100,
        long warId = 200) =>
        new(
            memberId,
            memberId,
            factionId,
            warId,
            state,
            Now.AddHours(-1),
            Now.AddHours(4),
            null,
            Now);

    private static void AssertDeclaration(
        WarReadinessDeclaration expected,
        WarReadinessDeclaration actual)
    {
        Assert.Equal(expected.FactionId, actual.FactionId);
        Assert.Equal(expected.WarId, actual.WarId);
        Assert.Equal(expected.MemberId, actual.MemberId);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.WindowStartUtc, actual.WindowStartUtc);
        Assert.Equal(expected.WindowEndUtc, actual.WindowEndUtc);
        Assert.Equal(expected.Note, actual.Note);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
        Assert.Equal(expected.Revision, actual.Revision);
    }

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
