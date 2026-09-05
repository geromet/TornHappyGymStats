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

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Approval_and_supersession_are_append_only_audit_events()
    {
        if (!_available)
            return;

        const long factionId = 7234;
        const long warId = 15876;
        var sourceRunId = Guid.Parse("2f5f14f2-ae3b-4df2-b2d7-85283bb652d0");
        var replacementRunId = Guid.Parse("1d7e94ea-33cb-4e38-a088-4cc67032ef80");
        var approvalEventId = Guid.Parse("6abf54cb-c64f-48ec-98cc-e27960955056");
        var replacementApprovalEventId = Guid.Parse("96f92849-44f3-4b31-a4cf-701d69130e64");
        var supersessionEventId = Guid.Parse("5310ad39-f7a0-4fe5-8612-c5ffc66c5e87");

        await using var db = CreateDbContext();
        var repository = new WarAccountingRunRepository(db);
        await repository.FreezeAsync(
            sourceRunId,
            factionId,
            warId,
            "freezer-one",
            DateTimeOffset.Parse("2026-09-05T08:30:00Z"),
            CancellationToken.None);
        await repository.FreezeAsync(
            replacementRunId,
            factionId,
            warId,
            "freezer-two",
            DateTimeOffset.Parse("2026-09-05T08:31:00Z"),
            CancellationToken.None);

        var approval = await repository.ApproveAsync(
            approvalEventId,
            sourceRunId,
            "admin-one",
            DateTimeOffset.Parse("2026-09-05T08:32:00Z"),
            "Reviewed against source ledger and policy.",
            CancellationToken.None);
        var replacementApproval = await repository.ApproveAsync(
            replacementApprovalEventId,
            replacementRunId,
            "admin-two",
            DateTimeOffset.Parse("2026-09-05T08:33:00Z"),
            "Replacement corrects an audited adjustment.",
            CancellationToken.None);
        var supersession = await repository.SupersedeAsync(
            supersessionEventId,
            sourceRunId,
            replacementRunId,
            "admin-three",
            DateTimeOffset.Parse("2026-09-05T08:34:00Z"),
            "Superseded by the approved corrected run.",
            CancellationToken.None);

        Assert.Equal(WarAccountingRunLifecycleKind.Approved, approval.Kind);
        Assert.Equal(WarAccountingRunLifecycleKind.Approved, replacementApproval.Kind);
        Assert.Equal(WarAccountingRunLifecycleKind.Superseded, supersession.Kind);
        Assert.Equal(replacementRunId, supersession.SupersedingRunId);

        var lifecycle = await repository.GetLifecycleAsync(sourceRunId, CancellationToken.None);
        Assert.Equal(new[] { approval, supersession }, lifecycle);

        await Assert.ThrowsAnyAsync<Exception>(() => repository.ApproveAsync(
            Guid.NewGuid(),
            sourceRunId,
            "late-admin",
            DateTimeOffset.Parse("2026-09-05T08:35:00Z"),
            "Attempted re-approval.",
            CancellationToken.None));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "WarAccountingRunLifecycleEvents"
            SET "Reason" = {{"rewritten audit reason"}}
            WHERE "EventId" = {{approvalEventId}}
            """));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM "WarAccountingRunLifecycleEvents"
            WHERE "EventId" = {{supersessionEventId}}
            """));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Supersession_rejects_unapproved_replacement_and_cross_scope_injection()
    {
        if (!_available)
            return;

        const long factionId = 8234;
        const long warId = 16876;
        var sourceRunId = Guid.Parse("c98609fe-2b72-4378-9fc4-f773b46b96c9");
        var replacementRunId = Guid.Parse("8cb896e3-143c-4763-844d-233741fd3c0a");
        var foreignRunId = Guid.Parse("185e396f-4dd7-41b4-91fc-4807e45271a7");

        await using var db = CreateDbContext();
        var repository = new WarAccountingRunRepository(db);
        await repository.FreezeAsync(
            sourceRunId,
            factionId,
            warId,
            "source-freezer",
            DateTimeOffset.Parse("2026-09-05T08:40:00Z"),
            CancellationToken.None);
        await repository.FreezeAsync(
            replacementRunId,
            factionId,
            warId,
            "replacement-freezer",
            DateTimeOffset.Parse("2026-09-05T08:41:00Z"),
            CancellationToken.None);
        await repository.FreezeAsync(
            foreignRunId,
            factionId + 1,
            warId,
            "foreign-freezer",
            DateTimeOffset.Parse("2026-09-05T08:41:00Z"),
            CancellationToken.None);

        await repository.ApproveAsync(
            Guid.NewGuid(),
            sourceRunId,
            "source-approver",
            DateTimeOffset.Parse("2026-09-05T08:42:00Z"),
            "Source approved.",
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => repository.SupersedeAsync(
            Guid.NewGuid(),
            sourceRunId,
            replacementRunId,
            "attacker",
            DateTimeOffset.Parse("2026-09-05T08:43:00Z"),
            "Replacement is not approved.",
            CancellationToken.None));

        await repository.ApproveAsync(
            Guid.NewGuid(),
            replacementRunId,
            "replacement-approver",
            DateTimeOffset.Parse("2026-09-05T08:44:00Z"),
            "Replacement approved.",
            CancellationToken.None);
        await repository.ApproveAsync(
            Guid.NewGuid(),
            foreignRunId,
            "foreign-approver",
            DateTimeOffset.Parse("2026-09-05T08:44:00Z"),
            "Foreign run approved.",
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => repository.SupersedeAsync(
            Guid.NewGuid(),
            sourceRunId,
            foreignRunId,
            "attacker",
            DateTimeOffset.Parse("2026-09-05T08:45:00Z"),
            "Attempted cross-faction replacement.",
            CancellationToken.None));

        var sourceLifecycle = await repository.GetLifecycleAsync(sourceRunId, CancellationToken.None);
        Assert.Single(sourceLifecycle);
        Assert.Equal(WarAccountingRunLifecycleKind.Approved, sourceLifecycle[0].Kind);
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
