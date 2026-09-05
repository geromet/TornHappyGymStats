using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HappyGymStats.Tests;

public sealed class WarAccountingSourcePostgresPersistenceTests : IAsyncLifetime
{
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
    public async Task Frozen_run_captures_exact_source_facts_and_later_report_changes_do_not_rewrite_them()
    {
        if (!_available)
            return;

        const long factionId = 9234;
        const long warId = 17876;
        var firstRunId = Guid.Parse("921232ec-9292-473f-a949-2a066de24936");
        var secondRunId = Guid.Parse("2e28a5b5-99b1-43d8-ac67-d73946716eaf");
        var capturedAt = DateTimeOffset.Parse("2026-09-05T09:10:00Z");

        await using var db = CreateDbContext();
        await InsertReportMemberAsync(db, factionId, warId, 3001, "Delta", 110, 4, 8, capturedAt);
        await InsertReportMemberAsync(db, factionId, warId, 3002, "Echo", 210, 7, 12, capturedAt.AddSeconds(1));

        var repository = new WarAccountingRunRepository(db);
        var firstRun = await repository.FreezeAsync(
            firstRunId,
            factionId,
            warId,
            "source-freezer-one",
            DateTimeOffset.Parse("2026-09-05T09:11:00Z"),
            CancellationToken.None);
        var firstSource = await repository.GetSourceAsync(firstRun.SourceSnapshotId, CancellationToken.None);

        Assert.NotNull(firstSource);
        Assert.Equal(firstRun.SourceSnapshotId, firstSource.SourceSnapshotId);
        Assert.Equal(2, firstSource.Members.Count);
        Assert.Equal(new long[] { 3001, 3002 }, firstSource.Members.Select(member => member.MemberId));
        Assert.Equal(
            firstSource.Fingerprint,
            HappyGymStats.Core.War.WarAccountingSourceFingerprint.Compute(factionId, warId, firstSource.Members));

        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "RankedWarReportMembers"
            SET "Score" = {{999}}
            WHERE "WarId" = {{warId}} AND "FactionId" = {{factionId}} AND "MemberId" = {{3002L}}
            """);

        var firstSourceAfterReportChange = await repository.GetSourceAsync(
            firstRun.SourceSnapshotId,
            CancellationToken.None);
        Assert.NotNull(firstSourceAfterReportChange);
        Assert.Equal(210, firstSourceAfterReportChange.Members.Single(member => member.MemberId == 3002).Score);
        Assert.Equal(firstSource.Fingerprint, firstSourceAfterReportChange.Fingerprint);

        var secondRun = await repository.FreezeAsync(
            secondRunId,
            factionId,
            warId,
            "source-freezer-two",
            DateTimeOffset.Parse("2026-09-05T09:12:00Z"),
            CancellationToken.None);
        var secondSource = await repository.GetSourceAsync(secondRun.SourceSnapshotId, CancellationToken.None);

        Assert.NotNull(secondSource);
        Assert.NotEqual(firstRun.SourceSnapshotId, secondRun.SourceSnapshotId);
        Assert.Equal(999, secondSource.Members.Single(member => member.MemberId == 3002).Score);
        Assert.NotEqual(firstSource.Fingerprint, secondSource.Fingerprint);
    }

    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Source_snapshot_and_member_facts_are_database_immutable_and_scope_bound()
    {
        if (!_available)
            return;

        const long factionId = 10234;
        const long warId = 18876;
        const long foreignFactionId = 10235;
        var sourceRunId = Guid.Parse("3643781c-9525-4f9b-814f-54f3766932cd");
        var foreignRunId = Guid.Parse("3bbba8d7-119b-4dcb-a3aa-b17b3028b965");
        var capturedAt = DateTimeOffset.Parse("2026-09-05T09:20:00Z");

        await using var db = CreateDbContext();
        await InsertReportMemberAsync(db, factionId, warId, 4001, "Foxtrot", 310, 10, 16, capturedAt);
        await InsertReportMemberAsync(db, foreignFactionId, warId, 5001, "Golf", 410, 12, 18, capturedAt);

        var repository = new WarAccountingRunRepository(db);
        var sourceRun = await repository.FreezeAsync(
            sourceRunId,
            factionId,
            warId,
            "scope-freezer-one",
            capturedAt.AddMinutes(1),
            CancellationToken.None);
        var foreignRun = await repository.FreezeAsync(
            foreignRunId,
            foreignFactionId,
            warId,
            "scope-freezer-two",
            capturedAt.AddMinutes(2),
            CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "WarAccountingSourceMemberFacts"
            SET "Score" = {{999}}
            WHERE "SourceSnapshotId" = {{sourceRun.SourceSnapshotId}} AND "MemberId" = {{4001L}}
            """));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            DELETE FROM "WarAccountingSourceSnapshots"
            WHERE "SourceSnapshotId" = {{sourceRun.SourceSnapshotId}}
            """));

        var crossScopeRunId = Guid.Parse("e9caaf87-f610-4b1e-9c49-8d512f017130");
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "WarAccountingRuns" (
                "RunId", "FactionId", "WarId", "ObjectiveVersion", "SourceSnapshotId", "FrozenBy", "FrozenAtUtc")
            VALUES (
                {{crossScopeRunId}}, {{factionId}}, {{warId}}, {{1}}, {{foreignRun.SourceSnapshotId}}, {{"attacker"}}, {{capturedAt.UtcDateTime}})
            """));

        var invalidObjectiveRunId = Guid.Parse("883ff6a7-3904-49c9-9e71-d50b63774af9");
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "WarAccountingRuns" (
                "RunId", "FactionId", "WarId", "ObjectiveVersion", "SourceSnapshotId", "FrozenBy", "FrozenAtUtc")
            VALUES (
                {{invalidObjectiveRunId}}, {{factionId}}, {{warId}}, {{999}}, {{sourceRun.SourceSnapshotId}}, {{"attacker"}}, {{capturedAt.UtcDateTime}})
            """));

        var wrongScopeMemberId = 4002L;
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "WarAccountingSourceMemberFacts" (
                "SourceSnapshotId", "FactionId", "WarId", "MemberId", "MemberName", "Score", "Chain", "Attacks", "CapturedAtUtc")
            VALUES (
                {{sourceRun.SourceSnapshotId}}, {{foreignFactionId}}, {{warId}}, {{wrongScopeMemberId}}, {{"Injected"}}, {{1}}, {{1}}, {{1}}, {{capturedAt.UtcDateTime}})
            """));

        var persistedSource = await repository.GetSourceAsync(sourceRun.SourceSnapshotId, CancellationToken.None);
        Assert.NotNull(persistedSource);
        Assert.Single(persistedSource.Members);
        Assert.Equal(310, persistedSource.Members[0].Score);
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
