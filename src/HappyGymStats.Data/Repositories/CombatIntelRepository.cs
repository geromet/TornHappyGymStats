using System.Data;
using System.Data.Common;
using HappyGymStats.Core.War;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class CombatIntelRepository(HappyGymStatsDbContext db) : ICombatIntelRepository
{
    public async Task AppendAsync(
        CombatIntelObservation observation,
        DateTimeOffset trustedReferenceTimeUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);
        EnsurePostgres();

        var latestAllowedProviderTime = trustedReferenceTimeUtc.ToUniversalTime()
            + CombatIntelObservation.MaxProviderFutureSkew;
        if (observation.FetchedAtUtc.ToUniversalTime() > latestAllowedProviderTime
            || observation.ObservedAtUtc.ToUniversalTime() > latestAllowedProviderTime)
        {
            throw new ArgumentException(
                "Observation provider timestamps exceed the trusted ingestion clock skew allowance.",
                nameof(observation));
        }

        await WithOpenConnectionAsync(async connection =>
        {
            if (await ObservationExistsAsync(connection, observation.ObservationId, ct))
            {
                throw new InvalidOperationException(
                    $"Combat-intel observation '{observation.ObservationId}' is already persisted.");
            }

            if (observation.SupersedesObservationId is not null)
            {
                var superseded = await ReadSupersessionIdentityAsync(
                    connection,
                    observation.SupersedesObservationId,
                    ct);

                if (superseded is null)
                {
                    throw new InvalidOperationException(
                        $"Superseded combat-intel observation '{observation.SupersedesObservationId}' does not exist.");
                }

                if (superseded.Value.PlayerId != observation.PlayerId)
                {
                    throw new InvalidOperationException(
                        "A combat-intel observation cannot supersede an observation for another player.");
                }

                if (superseded.Value.VisibilityScope != observation.VisibilityScope)
                {
                    throw new InvalidOperationException(
                        "A combat-intel observation cannot change visibility scope through supersession.");
                }

                if (observation.VisibilityScope != CombatIntelVisibilityScope.Public
                    && !string.Equals(
                        superseded.Value.VisibilityOwner,
                        observation.VisibilityOwner,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "A private combat-intel observation cannot supersede an observation owned by another visibility principal.");
                }
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "CombatIntelObservations" (
                    "ObservationId", "PlayerId", "Provider", "FetchedAtUtc", "ObservedAtUtc",
                    "Classification", "Value", "LowerBound", "UpperBound", "ProviderMetadata",
                    "VisibilityScope", "VisibilityOwner", "SupersedesObservationId")
                VALUES (
                    @observationId, @playerId, @provider, @fetchedAtUtc, @observedAtUtc,
                    @classification, @value, @lowerBound, @upperBound, @providerMetadata,
                    @visibilityScope, @visibilityOwner, @supersedesObservationId)
                """;
            AddParameter(command, "observationId", DbType.String, observation.ObservationId);
            AddParameter(command, "playerId", DbType.Int64, observation.PlayerId);
            AddParameter(command, "provider", DbType.String, observation.Provider);
            AddParameter(command, "fetchedAtUtc", DbType.DateTime, observation.FetchedAtUtc.UtcDateTime);
            AddParameter(command, "observedAtUtc", DbType.DateTime, observation.ObservedAtUtc.UtcDateTime);
            AddParameter(command, "classification", DbType.Int32, (int)observation.Classification);
            AddParameter(command, "value", DbType.Decimal, observation.Value);
            AddParameter(command, "lowerBound", DbType.Decimal, observation.LowerBound);
            AddParameter(command, "upperBound", DbType.Decimal, observation.UpperBound);
            AddParameter(command, "providerMetadata", DbType.String, observation.ProviderMetadata);
            AddParameter(command, "visibilityScope", DbType.Int32, (int)observation.VisibilityScope);
            AddParameter(command, "visibilityOwner", DbType.String, observation.VisibilityOwner);
            AddParameter(command, "supersedesObservationId", DbType.String, observation.SupersedesObservationId);
            await command.ExecuteNonQueryAsync(ct);
        }, ct);
    }

    public async Task<IReadOnlyList<CombatIntelObservation>> GetHistoryAsync(
        long playerId,
        string? provider,
        DateTimeOffset? observedSinceUtc,
        CancellationToken ct)
    {
        EnsurePostgres();
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId), playerId, "Player id must be positive.");
        }

        if (provider is not null && string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provider filter must be non-empty when supplied.", nameof(provider));
        }

        return await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    "ObservationId", "PlayerId", "Provider", "FetchedAtUtc", "ObservedAtUtc",
                    "Classification", "Value", "LowerBound", "UpperBound", "ProviderMetadata",
                    "VisibilityScope", "VisibilityOwner", "SupersedesObservationId"
                FROM "CombatIntelObservations"
                WHERE "PlayerId" = @playerId
                  AND (@provider IS NULL OR "Provider" = @provider)
                  AND (@observedSinceUtc IS NULL OR "ObservedAtUtc" >= @observedSinceUtc)
                ORDER BY "ObservedAtUtc" DESC, "FetchedAtUtc" DESC, "ObservationId"
                """;
            AddParameter(command, "playerId", DbType.Int64, playerId);
            AddParameter(command, "provider", DbType.String, provider);
            AddParameter(
                command,
                "observedSinceUtc",
                DbType.DateTime,
                observedSinceUtc?.ToUniversalTime().UtcDateTime);

            var observations = new List<CombatIntelObservation>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                observations.Add(CombatIntelObservation.Create(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    ToUtcOffset(reader.GetDateTime(3)),
                    ToUtcOffset(reader.GetDateTime(4)),
                    (CombatIntelClassification)reader.GetInt32(5),
                    GetNullableDecimal(reader, 6),
                    GetNullableDecimal(reader, 7),
                    GetNullableDecimal(reader, 8),
                    (CombatIntelVisibilityScope)reader.GetInt32(10),
                    GetNullableString(reader, 11),
                    GetNullableString(reader, 9),
                    GetNullableString(reader, 12)));
            }

            return (IReadOnlyList<CombatIntelObservation>)observations;
        }, ct);
    }

    private void EnsurePostgres()
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Combat-intel persistence currently requires the PostgreSQL/Npgsql provider.");
        }
    }

    private async Task<T> WithOpenConnectionAsync<T>(
        Func<DbConnection, Task<T>> action,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            return await action(connection);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private Task WithOpenConnectionAsync(
        Func<DbConnection, Task> action,
        CancellationToken ct) =>
        WithOpenConnectionAsync(async connection =>
        {
            await action(connection);
            return true;
        }, ct);

    private static async Task<bool> ObservationExistsAsync(
        DbConnection connection,
        string observationId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM "CombatIntelObservations"
                WHERE "ObservationId" = @observationId)
            """;
        AddParameter(command, "observationId", DbType.String, observationId);
        return (bool)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("PostgreSQL did not return the observation existence result."));
    }

    private static async Task<(long PlayerId, CombatIntelVisibilityScope VisibilityScope, string? VisibilityOwner)?>
        ReadSupersessionIdentityAsync(
            DbConnection connection,
            string observationId,
            CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "PlayerId", "VisibilityScope", "VisibilityOwner"
            FROM "CombatIntelObservations"
            WHERE "ObservationId" = @observationId
            """;
        AddParameter(command, "observationId", DbType.String, observationId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return (
            reader.GetInt64(0),
            (CombatIntelVisibilityScope)reader.GetInt32(1),
            GetNullableString(reader, 2));
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static decimal? GetNullableDecimal(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static string? GetNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
