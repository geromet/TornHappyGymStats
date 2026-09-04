using DotNet.Testcontainers.Builders;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace HappyGymStats.Tests;

public sealed class CombatIntelPostgresPersistenceTests : IAsyncLifetime
{
    private const string SkipEnvVar = "HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION";
    private const string RequireEnvVar = "HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION";
    private readonly ITestOutputHelper _output;
    private PostgreSqlContainer? _postgres;
    private string? _skipReason;

    public CombatIntelPostgresPersistenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!IsRequired() && IsTruthy(Environment.GetEnvironmentVariable(SkipEnvVar)))
        {
            _skipReason = $"{SkipEnvVar} is set; combat-intel PostgreSQL persistence proof intentionally skipped.";
            return;
        }

        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
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
            _skipReason = $"PostgreSQL combat-intel proof could not start Docker/Testcontainers: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact(DisplayName = "PostgresApiIntegration: combat intel preserves history and supersession ownership")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Combat_intel_round_trips_history_and_rejects_cross_boundary_supersession()
    {
        if (_skipReason is not null)
        {
            if (IsRequired())
            {
                Assert.True(false, $"{RequireEnvVar} is set, so this tier must run: {_skipReason}");
            }

            _output.WriteLine(_skipReason);
            return;
        }

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString())
            .Options;

        await using var db = new HappyGymStatsDbContext(options);
        await db.Database.MigrateAsync();
        var repository = new CombatIntelRepository(db);

        var trustedReference = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var original = CombatIntelObservation.CreateFromProvider(
            "intel-1",
            12345,
            "provider-a",
            trustedReference.AddMinutes(-1),
            trustedReference.AddMinutes(-2),
            trustedReference,
            CombatIntelClassification.Estimated,
            lowerBound: 1_000_000m,
            upperBound: 2_000_000m,
            visibilityScope: CombatIntelVisibilityScope.Faction,
            visibilityOwner: "faction-77",
            providerMetadata: "{\"source\":\"roundtrip\"}");

        await repository.AppendAsync(original, trustedReference, CancellationToken.None);

        var replacement = CombatIntelObservation.CreateFromProvider(
            "intel-2",
            12345,
            "provider-a",
            trustedReference,
            trustedReference.AddSeconds(-30),
            trustedReference,
            CombatIntelClassification.Exact,
            value: 1_500_000m,
            visibilityScope: CombatIntelVisibilityScope.Faction,
            visibilityOwner: "faction-77",
            providerMetadata: "{\"source\":\"replacement\"}",
            supersedesObservationId: "intel-1");

        await repository.AppendAsync(replacement, trustedReference, CancellationToken.None);

        var history = await repository.GetHistoryAsync(
            12345,
            "provider-a",
            trustedReference.AddHours(-1),
            CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal("intel-2", history[0].ObservationId);
        Assert.Equal("intel-1", history[0].SupersedesObservationId);
        Assert.Equal(1_500_000m, history[0].Value);
        Assert.Equal("faction-77", history[0].VisibilityOwner);
        Assert.Equal("{\"source\":\"roundtrip\"}", history[1].ProviderMetadata);

        var crossPlayer = CombatIntelObservation.CreateFromProvider(
            "intel-cross-player",
            99999,
            "provider-a",
            trustedReference,
            trustedReference,
            trustedReference,
            CombatIntelClassification.Exact,
            value: 2_000_000m,
            visibilityScope: CombatIntelVisibilityScope.Faction,
            visibilityOwner: "faction-77",
            supersedesObservationId: "intel-2");

        var crossPlayerError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AppendAsync(crossPlayer, trustedReference, CancellationToken.None));
        Assert.Contains("another player", crossPlayerError.Message, StringComparison.OrdinalIgnoreCase);

        var crossOwner = CombatIntelObservation.CreateFromProvider(
            "intel-cross-owner",
            12345,
            "provider-a",
            trustedReference,
            trustedReference,
            trustedReference,
            CombatIntelClassification.Exact,
            value: 2_100_000m,
            visibilityScope: CombatIntelVisibilityScope.Faction,
            visibilityOwner: "faction-88",
            supersedesObservationId: "intel-2");

        var crossOwnerError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AppendAsync(crossOwner, trustedReference, CancellationToken.None));
        Assert.Contains("visibility principal", crossOwnerError.Message, StringComparison.OrdinalIgnoreCase);

        var bypassedProviderClockValidation = CombatIntelObservation.Create(
            "intel-future",
            12345,
            "provider-a",
            trustedReference.AddMinutes(5),
            trustedReference.AddMinutes(5),
            CombatIntelClassification.Exact,
            value: 3_000_000m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.AppendAsync(bypassedProviderClockValidation, trustedReference, CancellationToken.None));

        var historyAfterRejectedWrites = await repository.GetHistoryAsync(
            12345,
            null,
            null,
            CancellationToken.None);
        Assert.Equal(2, historyAfterRejectedWrites.Count);

        var indexNames = new HashSet<string>(StringComparer.Ordinal);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT indexname FROM pg_indexes WHERE tablename = 'CombatIntelObservations'";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexNames.Add(reader.GetString(0));
            }
        }

        Assert.Contains("IX_CombatIntelObservations_PlayerId_ObservedAtUtc", indexNames);
        Assert.Contains("IX_CombatIntelObservations_Provider_ObservedAtUtc", indexNames);
        Assert.Contains("IX_CombatIntelObservations_Provider_FetchedAtUtc", indexNames);
        Assert.Contains("IX_CombatIntelObservations_PlayerId_Provider_ObservedAtUtc", indexNames);
        Assert.Contains("IX_CombatIntel_Visibility_Owner_ObservedAtUtc", indexNames);
    }

    private static bool IsRequired() => IsTruthy(Environment.GetEnvironmentVariable(RequireEnvVar));

    private static bool IsTruthy(string? raw) =>
        string.Equals(raw, "1", StringComparison.Ordinal)
        || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
}
