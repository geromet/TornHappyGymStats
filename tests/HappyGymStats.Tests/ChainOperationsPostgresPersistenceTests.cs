using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HappyGymStats.Tests;

public sealed class ChainOperationsPostgresPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirstShiftId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondShiftId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid SlotId = Guid.Parse("20000000-0000-0000-0000-000000000001");
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
    public async Task Operational_snapshot_survives_restart_without_losing_ordered_state()
    {
        if (!_available)
            return;

        var expected = Snapshot(factionId: 100, warId: 200, revision: 1);
        await using (var writerDb = CreateDbContext())
        {
            var writer = new ChainOperationsRepository(writerDb);
            await writer.SaveAsync(expected, expectedRevision: 0, CancellationToken.None);
        }

        await using var restartedDb = CreateDbContext();
        var restarted = new ChainOperationsRepository(restartedDb);
        var actual = Assert.IsType<ChainOperationsSnapshot>(
            await restarted.GetAsync(100, 200, CancellationToken.None));

        Assert.Equal(1, actual.Revision);
        Assert.Equal(new[] { FirstShiftId, SecondShiftId }, actual.Shifts.Select(item => item.Id));
        Assert.Equal(FirstShiftId, Assert.Single(actual.CheckIns).ShiftId);
        Assert.Equal(FirstShiftId, Assert.Single(actual.CheckOuts).ShiftId);
        Assert.Equal((FirstShiftId, SecondShiftId),
            (Assert.Single(actual.Handoffs).FromShiftId, Assert.Single(actual.Handoffs).ToShiftId));
        Assert.Equal(new long[] { 41, 42, 43 }, Assert.Single(actual.AttackSlots).CandidateOrder);
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Replay_and_stale_writer_are_rejected_without_duplicating_operational_state()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();
        var repository = new ChainOperationsRepository(db);
        var first = Snapshot(100, 200, revision: 1);
        await repository.SaveAsync(first, 0, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(first, 0, CancellationToken.None));

        var second = Snapshot(100, 200, revision: 2, includeSecondCheckIn: true);
        await repository.SaveAsync(second, 1, CancellationToken.None);

        var stale = Snapshot(100, 200, revision: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(stale, 1, CancellationToken.None));

        var restored = Assert.IsType<ChainOperationsSnapshot>(
            await repository.GetAsync(100, 200, CancellationToken.None));
        Assert.Equal(2, restored.Revision);
        Assert.Equal(2, restored.CheckIns.Count);
        Assert.Equal(restored.CheckIns.Count, restored.CheckIns.Distinct().Count());
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Reads_are_faction_and_war_scoped()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();
        var repository = new ChainOperationsRepository(db);
        await repository.SaveAsync(Snapshot(100, 200, 1), 0, CancellationToken.None);
        await repository.SaveAsync(Snapshot(101, 200, 1), 0, CancellationToken.None);
        await repository.SaveAsync(Snapshot(100, 201, 1), 0, CancellationToken.None);

        Assert.NotNull(await repository.GetAsync(100, 200, CancellationToken.None));
        Assert.NotNull(await repository.GetAsync(101, 200, CancellationToken.None));
        Assert.NotNull(await repository.GetAsync(100, 201, CancellationToken.None));
        Assert.Null(await repository.GetAsync(101, 201, CancellationToken.None));
    }

    [Fact]
    public void Snapshot_rejects_duplicate_or_out_of_scope_restart_events()
    {
        var shift = new WatcherShift(FirstShiftId, 11, Now, Now.AddHours(1));
        var duplicate = new WatcherCheckIn(FirstShiftId, Now.AddMinutes(1));

        Assert.Throws<ArgumentException>(() => new ChainOperationsSnapshot(
            100, 200, 1, [shift], [duplicate, duplicate]));

        Assert.Throws<ArgumentException>(() => new ChainOperationsSnapshot(
            100, 200, 1, [shift],
            [new WatcherCheckIn(SecondShiftId, Now.AddMinutes(1))]));
    }

    private static ChainOperationsSnapshot Snapshot(
        long factionId,
        long warId,
        long revision,
        bool includeSecondCheckIn = false)
    {
        var first = new WatcherShift(FirstShiftId, 11, Now, Now.AddHours(1));
        var second = new WatcherShift(SecondShiftId, 12, Now.AddHours(1), Now.AddHours(2));
        var checkIns = new List<WatcherCheckIn>
        {
            new(FirstShiftId, Now.AddMinutes(1))
        };
        if (includeSecondCheckIn)
            checkIns.Add(new WatcherCheckIn(SecondShiftId, Now.AddHours(1).AddMinutes(1)));

        return new ChainOperationsSnapshot(
            factionId,
            warId,
            revision,
            [first, second],
            checkIns,
            [new WatcherCheckOut(FirstShiftId, Now.AddHours(1))],
            [new WatcherHandoff(FirstShiftId, SecondShiftId, Now.AddHours(1))],
            [new ChainAttackSlot(SlotId, Now.AddHours(3), 41, [42, 43])]);
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
