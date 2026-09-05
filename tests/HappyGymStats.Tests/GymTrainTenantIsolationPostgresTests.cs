using DotNet.Testcontainers.Builders;
using HappyGymStats.Core.Models;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace HappyGymStats.Tests;

public sealed class GymTrainTenantIsolationPostgresTests : IAsyncLifetime
{
    private const string SkipEnvVar = "HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION";
    private const string RequireEnvVar = "HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION";
    private readonly ITestOutputHelper _output;
    private PostgreSqlContainer? _postgres;
    private string? _skipReason;

    public GymTrainTenantIsolationPostgresTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!IsRequired() && IsTruthy(Environment.GetEnvironmentVariable(SkipEnvVar)))
        {
            _skipReason = $"{SkipEnvVar} is set; gym-train PostgreSQL tenant proof intentionally skipped.";
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
            _skipReason = $"PostgreSQL gym-train tenant proof could not start Docker/Testcontainers: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact(DisplayName = "PostgresApiIntegration: gym train cursor paging stays tenant scoped")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Gym_train_cursor_paging_stays_tenant_scoped()
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

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.MigrateAsync();

        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var tiedAt = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        db.UserLogEntries.AddRange(
            Train(ownerB, "other-newest", tiedAt.AddHours(1), 900, 100),
            Train(ownerA, "train-c", tiedAt, 300, 50),
            Train(ownerA, "train-b", tiedAt, 280, 40),
            Train(ownerA, "train-a", tiedAt.AddMinutes(-15), 260, 40));
        await db.SaveChangesAsync();

        var repository = new UserLogEntryRepository(db);
        var firstPage = await repository.GetGymTrainsPageAsync(ownerA, 2, null, CancellationToken.None);

        Assert.Equal(new[] { "train-c", "train-b" }, firstPage.Items.Select(x => x.LogId).ToArray());
        Assert.DoesNotContain(firstPage.Items, x => x.LogId == "other-newest");
        Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));
        Assert.True(CursorEncoder.TryDecode(firstPage.NextCursor, out var cursor));
        Assert.NotNull(cursor);

        var secondPage = await repository.GetGymTrainsPageAsync(ownerA, 2, cursor, CancellationToken.None);

        Assert.Equal(new[] { "train-a" }, secondPage.Items.Select(x => x.LogId).ToArray());
        Assert.DoesNotContain(secondPage.Items, x => x.LogId == "other-newest");
        Assert.Null(secondPage.NextCursor);
    }

    private static UserLogEntryEntity Train(Guid owner, string id, DateTimeOffset occurredAtUtc, int happyBefore, int happyUsed)
        => new()
        {
            AnonymousId = owner,
            LogEntryId = id,
            OccurredAtUtc = occurredAtUtc,
            LogTypeId = 1,
            HappyBeforeTrain = happyBefore,
            HappyUsed = happyUsed,
        };

    private static bool IsRequired()
        => IsTruthy(Environment.GetEnvironmentVariable(RequireEnvVar));

    private static bool IsTruthy(string? value)
        => value is not null && value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
