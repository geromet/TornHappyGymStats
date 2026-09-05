using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HappyGymStats.Tests;

public sealed class WarPayoutPostgresPersistenceTests : IAsyncLifetime
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
    public async Task Policy_history_is_versioned_bounded_and_database_immutable()
    {
        if (!_available)
            return;

        const long factionId = 13001;
        const long warId = 23001;
        await using var db = CreateDbContext();
        var repository = new WarPayoutRepository(db);

        var first = await repository.AppendPolicyAsync(
            factionId, warId, 10m, 2m, 1m, 25m, "policy-admin-one",
            DateTimeOffset.Parse("2026-09-05T09:30:00Z"), CancellationToken.None);
        var second = await repository.AppendPolicyAsync(
            factionId, warId, 12m, 3m, 1.5m, 50m, "policy-admin-two",
            DateTimeOffset.Parse("2026-09-05T09:31:00Z"), CancellationToken.None);

        Assert.Equal(1, first.Policy.Version);
        Assert.Equal(2, second.Policy.Version);
        var history = await repository.GetPolicyHistoryAsync(factionId, warId, CancellationToken.None);
        Assert.Equal(new[] { 1, 2 }, history.Select(item => item.Policy.Version));
        Assert.Equal(10m, history[0].Policy.ScoreRate);
        Assert.Equal(12m, history[1].Policy.ScoreRate);

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "WarPayoutPolicyVersions"
            SET "ScoreRate" = {{999m}}
            WHERE "FactionId" = {{factionId}} AND "WarId" = {{warId}} AND "Version" = {{1}}
            """));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM "WarPayoutPolicyVersions"
            WHERE "FactionId" = {{factionId}} AND "WarId" = {{warId}} AND "Version" = {{1}}
            """));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "WarPayoutPolicyVersions" (
                "FactionId", "WarId", "Version", "ScoreRate", "ChainRate", "AttackRate",
                "FixedMemberAmount", "ChangedBy", "CreatedAtUtc")
            VALUES ({{factionId}}, {{warId}}, {{3}}, {{-1m}}, {{0m}}, {{0m}}, {{0m}}, {{"attacker"}}, {{DateTime.UtcNow}})
            """));
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Frozen_payout_binds_exact_run_source_policy_and_preserves_residual()
    {
        if (!_available)
            return;

        const long factionId = 13002;
        const long warId = 23002;
        var runId = Guid.Parse("258a57ef-65c4-4ef0-a133-d9617d59faad");
        var capturedAt = DateTimeOffset.Parse("2026-09-05T09:40:00Z");

        await using var db = CreateDbContext();
        await InsertReportMemberAsync(db, factionId, warId, 8101, "Hotel", 100, 10, 5, capturedAt);
        await InsertReportMemberAsync(db, factionId, warId, 8102, "India", 200, 20, 8, capturedAt.AddSeconds(1));

        var runRepository = new WarAccountingRunRepository(db);
        var run = await runRepository.FreezeAsync(
            runId, factionId, warId, "run-freezer", capturedAt.AddMinutes(1), CancellationToken.None);
        var payoutRepository = new WarPayoutRepository(db);
        var policy1 = await payoutRepository.AppendPolicyAsync(
            factionId, warId, 10m, 2m, 1m, 25m, "policy-one", capturedAt.AddMinutes(2), CancellationToken.None);
        var policy2 = await payoutRepository.AppendPolicyAsync(
            factionId, warId, 99m, 0m, 0m, 0m, "policy-two", capturedAt.AddMinutes(3), CancellationToken.None);

        var frozen = await payoutRepository.CalculateAndFreezeAsync(
            runId, policy1.Policy.Version, 10_000m, "calculator-admin", capturedAt.AddMinutes(4), CancellationToken.None);

        Assert.Equal(run.SourceSnapshotId, frozen.SourceSnapshotId);
        Assert.Equal(1, frozen.PolicyVersion);
        Assert.Equal(3_103m, frozen.AllocatedAmount);
        Assert.Equal(6_897m, frozen.UnattributedResidual);
        Assert.Equal(frozen.PoolAmount, frozen.AllocatedAmount + frozen.UnattributedResidual);
        Assert.Equal(new long[] { 8101, 8102 }, frozen.Lines.Select(line => line.MemberId));
        Assert.Equal(2, policy2.Policy.Version);

        var persisted = await payoutRepository.GetFrozenAsync(runId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(frozen.SourceSnapshotId, persisted.SourceSnapshotId);
        Assert.Equal(1, persisted.PolicyVersion);
        Assert.Equal(frozen.AllocatedAmount, persisted.AllocatedAmount);
        Assert.True(frozen.Lines.SequenceEqual(persisted.Lines));

        await Assert.ThrowsAnyAsync<Exception>(() => payoutRepository.CalculateAndFreezeAsync(
            runId, policy2.Policy.Version, 100_000m, "second-calculator", capturedAt.AddMinutes(5), CancellationToken.None));
        var stillFrozen = await payoutRepository.GetFrozenAsync(runId, CancellationToken.None);
        Assert.Equal(1, stillFrozen!.PolicyVersion);
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Database_rejects_cross_scope_policy_source_and_beneficiary_injection_and_mutation()
    {
        if (!_available)
            return;

        const long factionId = 13003;
        const long foreignFactionId = 13004;
        const long warId = 23003;
        var sourceRunId = Guid.Parse("d2168ad4-dc57-49ed-8ed1-f70c7f12cb82");
        var foreignRunId = Guid.Parse("883e0eb6-0a43-4134-bb61-4295f9e7a4d2");
        var capturedAt = DateTimeOffset.Parse("2026-09-05T09:50:00Z");

        await using var db = CreateDbContext();
        await InsertReportMemberAsync(db, factionId, warId, 8201, "Juliet", 100, 1, 1, capturedAt);
        await InsertReportMemberAsync(db, foreignFactionId, warId, 9201, "Kilo", 100, 1, 1, capturedAt);
        var runRepository = new WarAccountingRunRepository(db);
        var sourceRun = await runRepository.FreezeAsync(
            sourceRunId, factionId, warId, "source", capturedAt.AddMinutes(1), CancellationToken.None);
        var foreignRun = await runRepository.FreezeAsync(
            foreignRunId, foreignFactionId, warId, "foreign", capturedAt.AddMinutes(1), CancellationToken.None);

        var payoutRepository = new WarPayoutRepository(db);
        var policy = await payoutRepository.AppendPolicyAsync(
            factionId, warId, 1m, 0m, 0m, 0m, "policy", capturedAt.AddMinutes(2), CancellationToken.None);
        await payoutRepository.AppendPolicyAsync(
            foreignFactionId, warId, 1m, 0m, 0m, 0m, "foreign-policy", capturedAt.AddMinutes(2), CancellationToken.None);
        await payoutRepository.CalculateAndFreezeAsync(
            sourceRunId, policy.Policy.Version, 1_000m, "calculator", capturedAt.AddMinutes(3), CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "WarPayoutReconciliations"
            SET "PoolAmount" = {{999m}}
            WHERE "RunId" = {{sourceRunId}}
            """));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM "WarPayoutLines"
            WHERE "RunId" = {{sourceRunId}}
            """));

        var injectedRunId = Guid.Parse("3ccb1afd-cb05-4b45-8f8d-492c3ec784da");
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "WarPayoutReconciliations" (
                "RunId", "FactionId", "WarId", "SourceSnapshotId", "PolicyVersion", "PoolAmount",
                "AllocatedAmount", "UnattributedResidual", "CalculatedBy", "CalculatedAtUtc")
            VALUES (
                {{injectedRunId}}, {{foreignFactionId}}, {{warId}}, {{foreignRun.SourceSnapshotId}}, {{1}}, {{100m}},
                {{0m}}, {{100m}}, {{"attacker"}}, {{capturedAt.UtcDateTime}})
            """));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "WarPayoutLines" (
                "RunId", "SourceSnapshotId", "FactionId", "WarId", "MemberId", "MemberName",
                "Score", "Chain", "Attacks", "ScoreAmount", "ChainAmount", "AttackAmount", "FixedAmount", "TotalAmount")
            VALUES (
                {{sourceRunId}}, {{foreignRun.SourceSnapshotId}}, {{foreignFactionId}}, {{warId}}, {{9201L}}, {{"Kilo"}},
                {{100}}, {{1}}, {{1}}, {{1m}}, {{0m}}, {{0m}}, {{0m}}, {{1m}})
            """));
    }

    private static Task<int> InsertReportMemberAsync(
        HappyGymStatsDbContext db,
        long factionId,
        long warId,
        long memberId,
        string memberName,
        int score,
        int chain,
        int attacks,
        DateTimeOffset capturedAtUtc)
        => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "RankedWarReportMembers" (
                "WarId", "FactionId", "MemberId", "Attacks", "CapturedAtUtc", "Chain",
                "FactionName", "IngestedAtUtc", "IsIdleAttacker", "MemberName", "Score")
            VALUES (
                {{warId}}, {{factionId}}, {{memberId}}, {{attacks}}, {{capturedAtUtc.UtcDateTime}}, {{chain}},
                {{"Fixture Faction"}}, {{capturedAtUtc.UtcDateTime}}, {{false}}, {{memberName}}, {{score}})
            """);

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
