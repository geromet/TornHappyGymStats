using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HappyGymStats.Tests;

public sealed class WarAccountingRunPostgresPersistenceTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private bool _available;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
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
    public async Task Frozen_run_binds_exact_objective_version_even_after_later_append()
    {
        if (!_available)
            return;

        const long factionId = 4234;
        const long warId = 12876;
        var runId = Guid.Parse("bca4bef0-e90a-4c8d-a447-b735a9dd0b8d");
        var frozenAt = DateTimeOffset.Parse("2026-09-05T08:00:00Z");

        await using var db = CreateDbContext();
        var objectiveRepository = new WarObjectiveRepository(db);
        var runRepository = new WarAccountingRunRepository(db);

        var objective = await objectiveRepository.AppendNextAsync(
            factionId,
            warId,
            WarObjectiveMode.TermedWin,
            changedBy: "leader-before-freeze",
            createdAtUtc: DateTimeOffset.Parse("2026-09-05T07:55:00Z"),
            stopAtFactionScore: 6500,
            notes: "frozen terms",
            CancellationToken.None);
        Assert.Equal(2, objective.Objective.Version);

        var frozen = await runRepository.FreezeAsync(
            runId,
            factionId,
            warId,
            "approver-one",
            frozenAt,
            CancellationToken.None);

        Assert.Equal(2, frozen.ObjectiveVersion);
        Assert.Equal("approver-one", frozen.FrozenBy);
        Assert.Equal(frozenAt, frozen.FrozenAtUtc);

        var replacement = await objectiveRepository.AppendNextAsync(
            factionId,
            warId,
            WarObjectiveMode.TermedLoss,
            changedBy: "leader-after-freeze",
            createdAtUtc: DateTimeOffset.Parse("2026-09-05T08:05:00Z"),
            stopAtFactionScore: 7000,
            notes: "later terms",
            CancellationToken.None);
        Assert.Equal(3, replacement.Objective.Version);

        var persisted = await runRepository.GetAsync(runId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(2, persisted.ObjectiveVersion);
        Assert.Equal(frozen, persisted);
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Frozen_run_tuple_is_fk_bound_and_database_immutable()
    {
        if (!_available)
            return;

        const long factionId = 5234;
        const long warId = 13876;
        var runId = Guid.Parse("a2655da9-fb8b-4a76-b64d-2fb52b9f6b79");

        await using var db = CreateDbContext();
        var runRepository = new WarAccountingRunRepository(db);
        var frozen = await runRepository.FreezeAsync(
            runId,
            factionId,
            warId,
            "approver-two",
            DateTimeOffset.Parse("2026-09-05T08:10:00Z"),
            CancellationToken.None);
        Assert.Equal(1, frozen.ObjectiveVersion);

        var invalidRunId = Guid.Parse("7826bc23-22e4-49a1-b651-81f274aacd2e");
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "WarAccountingRuns" (
                "RunId", "FactionId", "WarId", "ObjectiveVersion", "FrozenBy", "FrozenAtUtc")
            VALUES (
                {{invalidRunId}}, {{factionId}}, {{warId}}, {{999}}, {{"attacker"}}, {{DateTime.UtcNow}})
            """));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "WarAccountingRuns"
            SET "ObjectiveVersion" = 999
            WHERE "RunId" = {{runId}}
            """));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM "WarAccountingRuns"
            WHERE "RunId" = {{runId}}
            """));

        var persisted = await runRepository.GetAsync(runId, CancellationToken.None);
        Assert.Equal(frozen, persisted);
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Freeze_and_objective_append_serialize_to_a_real_persisted_version()
    {
        if (!_available)
            return;

        const long factionId = 6234;
        const long warId = 14876;
        var runId = Guid.Parse("fe550f64-fcaa-4f74-8716-fcba73aceafe");

        await using var freezeDb = CreateDbContext();
        await using var appendDb = CreateDbContext();
        var runRepository = new WarAccountingRunRepository(freezeDb);
        var objectiveRepository = new WarObjectiveRepository(appendDb);

        var freezeTask = runRepository.FreezeAsync(
            runId,
            factionId,
            warId,
            "approver-race",
            DateTimeOffset.Parse("2026-09-05T08:20:00Z"),
            CancellationToken.None);
        var appendTask = objectiveRepository.AppendNextAsync(
            factionId,
            warId,
            WarObjectiveMode.TermedWin,
            changedBy: "leader-race",
            createdAtUtc: DateTimeOffset.Parse("2026-09-05T08:20:01Z"),
            stopAtFactionScore: 8000,
            notes: "concurrent terms",
            CancellationToken.None);

        await Task.WhenAll(freezeTask, appendTask);

        var frozen = await freezeTask;
        var appended = await appendTask;
        Assert.Equal(2, appended.Objective.Version);
        Assert.Contains(frozen.ObjectiveVersion, new[] { 1, 2 });

        await using var verifyDb = CreateDbContext();
        var verifyObjectiveRepository = new WarObjectiveRepository(verifyDb);
        var verifyRunRepository = new WarAccountingRunRepository(verifyDb);
        var history = await verifyObjectiveRepository.GetHistoryAsync(factionId, warId, CancellationToken.None);
        Assert.Equal(new[] { 1, 2 }, history.Select(item => item.Objective.Version));
        Assert.Contains(history, item => item.Objective.Version == frozen.ObjectiveVersion);

        var persisted = await verifyRunRepository.GetAsync(runId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(frozen.ObjectiveVersion, persisted.ObjectiveVersion);
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
