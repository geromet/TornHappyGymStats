using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HappyGymStats.Tests;

public sealed class WarObjectivePostgresPersistenceTests : IAsyncLifetime
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
    public async Task Effective_default_is_version_one_and_explicit_changes_append_immutable_history()
    {
        if (!_available)
            return;

        await using var db = CreateDbContext();
        var repository = new WarObjectiveRepository(db);

        var effectiveDefault = await repository.GetEffectiveAsync(1234, 9876, CancellationToken.None);
        Assert.Equal(1234, effectiveDefault.FactionId);
        Assert.Equal(1, effectiveDefault.Objective.Version);
        Assert.Equal(WarObjectiveMode.CompetitiveWin, effectiveDefault.Objective.Mode);
        Assert.False(effectiveDefault.Objective.IsExplicit);
        Assert.Null(effectiveDefault.Objective.StopAtFactionScore);
        Assert.Equal("system", effectiveDefault.Objective.ChangedBy);
        Assert.Equal(DateTimeOffset.UnixEpoch, effectiveDefault.Objective.CreatedAtUtc);
        Assert.Empty(await repository.GetHistoryAsync(1234, 9876, CancellationToken.None));

        var firstExplicit = await repository.AppendNextAsync(
            factionId: 1234,
            warId: 9876,
            WarObjectiveMode.TermedWin,
            changedBy: "leader-one",
            createdAtUtc: DateTimeOffset.Parse("2026-09-05T01:00:00Z"),
            stopAtFactionScore: 2500,
            notes: "initial terms",
            CancellationToken.None);
        var secondExplicit = await repository.AppendNextAsync(
            factionId: 1234,
            warId: 9876,
            WarObjectiveMode.TermedLoss,
            changedBy: "leader-two",
            createdAtUtc: DateTimeOffset.Parse("2026-09-05T01:05:00Z"),
            stopAtFactionScore: 3000,
            notes: "replacement terms",
            CancellationToken.None);

        Assert.Equal(2, firstExplicit.Objective.Version);
        Assert.Equal(3, secondExplicit.Objective.Version);

        var current = await repository.GetCurrentAsync(1234, 9876, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(3, current.Objective.Version);
        Assert.Equal("leader-two", current.Objective.ChangedBy);

        var effectiveCurrent = await repository.GetEffectiveAsync(1234, 9876, CancellationToken.None);
        Assert.Equal(3, effectiveCurrent.Objective.Version);
        Assert.True(effectiveCurrent.Objective.IsExplicit);

        var history = await repository.GetHistoryAsync(1234, 9876, CancellationToken.None);
        Assert.Equal(new[] { 1, 2, 3 }, history.Select(item => item.Objective.Version));
        Assert.Equal(
            new[] { false, true, true },
            history.Select(item => item.Objective.IsExplicit));
        Assert.Equal(
            new[] { WarObjectiveMode.CompetitiveWin, WarObjectiveMode.TermedWin, WarObjectiveMode.TermedLoss },
            history.Select(item => item.Objective.Mode));
        Assert.Equal(
            new string?[] { null, "initial terms", "replacement terms" },
            history.Select(item => item.Objective.Notes));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"WarObjectiveVersions\" SET \"Notes\" = 'tampered' WHERE \"FactionId\" = 1234 AND \"WarId\" = 9876 AND \"Version\" = 1"));
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
