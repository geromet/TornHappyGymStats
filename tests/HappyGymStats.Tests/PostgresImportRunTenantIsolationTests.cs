using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class PostgresImportRunTenantIsolationTests
{
    [Fact]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Incomplete_resume_lookup_never_crosses_owner_boundary()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("happygymstats_resume_scope")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var started = new DateTimeOffset(2026, 9, 4, 18, 0, 0, TimeSpan.Zero);

        var ownerARun = new ImportRunEntity
        {
            AnonymousId = ownerA,
            StartedAtUtc = started,
            Outcome = "running",
            NextUrl = "https://api.torn.com/v2/user/log?cursor=owner-a",
        };
        var ownerBNewerRun = new ImportRunEntity
        {
            AnonymousId = ownerB,
            StartedAtUtc = started.AddMinutes(10),
            Outcome = "running",
            NextUrl = "https://api.torn.com/v2/user/log?cursor=owner-b",
        };
        var ownerACompletedNewerRun = new ImportRunEntity
        {
            AnonymousId = ownerA,
            StartedAtUtc = started.AddMinutes(20),
            CompletedAtUtc = started.AddMinutes(21),
            Outcome = "completed",
            NextUrl = null,
        };

        db.ImportRuns.AddRange(ownerARun, ownerBNewerRun, ownerACompletedNewerRun);
        await db.SaveChangesAsync();

        var repository = new ImportRunRepository(db);

        var resolvedForA = await repository.GetLatestIncompleteAsync(ownerA, CancellationToken.None);
        var resolvedForB = await repository.GetLatestIncompleteAsync(ownerB, CancellationToken.None);
        var resolvedForUnknown = await repository.GetLatestIncompleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(resolvedForA);
        Assert.Equal(ownerA, resolvedForA!.AnonymousId);
        Assert.Equal(ownerARun.NextUrl, resolvedForA.NextUrl);
        Assert.NotEqual(ownerBNewerRun.NextUrl, resolvedForA.NextUrl);

        Assert.NotNull(resolvedForB);
        Assert.Equal(ownerB, resolvedForB!.AnonymousId);
        Assert.Equal(ownerBNewerRun.NextUrl, resolvedForB.NextUrl);

        Assert.Null(resolvedForUnknown);
        Assert.Throws<ArgumentException>(() => repository.GetLatestIncompleteAsync(Guid.Empty, CancellationToken.None));
    }
}
