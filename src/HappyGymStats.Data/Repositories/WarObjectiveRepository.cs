using System.Data;
using System.Data.Common;
using HappyGymStats.Core.War;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class WarObjectiveRepository(HappyGymStatsDbContext db) : IWarObjectiveRepository
{
    public async Task<FactionWarObjectiveVersion> AppendNextAsync(
        long factionId,
        long warId,
        WarObjectiveMode mode,
        string changedBy,
        DateTimeOffset createdAtUtc,
        int? stopAtFactionScore,
        string? notes,
        CancellationToken ct)
    {
        ValidateIds(factionId, warId);
        EnsurePostgres();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            // Serialize writers for one faction/war even when the first row does not yet exist.
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.Transaction = transaction;
                lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
                AddParameter(lockCommand, "key", DbType.String, $"war-objective:{factionId}:{warId}");
                await lockCommand.ExecuteNonQueryAsync(ct);
            }

            var latest = await ReadLatestAsync(connection, transaction, factionId, warId, ct);
            var nextVersion = latest?.Objective.Version + 1 ?? 1;
            var objective = WarObjectiveVersion.Restore(
                warId,
                nextVersion,
                mode,
                isExplicit: true,
                stopAtFactionScore,
                notes,
                changedBy,
                createdAtUtc);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO "WarObjectiveVersions" (
                        "FactionId", "WarId", "Version", "Mode", "IsExplicit",
                        "StopAtFactionScore", "Notes", "ChangedBy", "CreatedAtUtc")
                    VALUES (
                        @factionId, @warId, @version, @mode, @isExplicit,
                        @stopAtFactionScore, @notes, @changedBy, @createdAtUtc)
                    """;
                AddParameter(command, "factionId", DbType.Int64, factionId);
                AddParameter(command, "warId", DbType.Int64, objective.WarId);
                AddParameter(command, "version", DbType.Int32, objective.Version);
                AddParameter(command, "mode", DbType.Int32, (int)objective.Mode);
                AddParameter(command, "isExplicit", DbType.Boolean, objective.IsExplicit);
                AddParameter(command, "stopAtFactionScore", DbType.Int32, objective.StopAtFactionScore);
                AddParameter(command, "notes", DbType.String, objective.Notes);
                AddParameter(command, "changedBy", DbType.String, objective.ChangedBy);
                AddParameter(command, "createdAtUtc", DbType.DateTime, objective.CreatedAtUtc.UtcDateTime);
                await command.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new FactionWarObjectiveVersion(factionId, objective);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<FactionWarObjectiveVersion?> GetCurrentAsync(
        long factionId,
        long warId,
        CancellationToken ct)
    {
        ValidateIds(factionId, warId);
        EnsurePostgres();

        return await WithOpenConnectionAsync(
            connection => ReadLatestAsync(connection, transaction: null, factionId, warId, ct),
            ct);
    }

    public async Task<IReadOnlyList<FactionWarObjectiveVersion>> GetHistoryAsync(
        long factionId,
        long warId,
        CancellationToken ct)
    {
        ValidateIds(factionId, warId);
        EnsurePostgres();

        return await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "FactionId", "WarId", "Version", "Mode", "IsExplicit",
                       "StopAtFactionScore", "Notes", "ChangedBy", "CreatedAtUtc"
                FROM "WarObjectiveVersions"
                WHERE "FactionId" = @factionId AND "WarId" = @warId
                ORDER BY "Version" ASC
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);

            var versions = new List<FactionWarObjectiveVersion>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                versions.Add(ReadVersion(reader));

            return (IReadOnlyList<FactionWarObjectiveVersion>)versions;
        }, ct);
    }

    private static async Task<FactionWarObjectiveVersion?> ReadLatestAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long factionId,
        long warId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT "FactionId", "WarId", "Version", "Mode", "IsExplicit",
                   "StopAtFactionScore", "Notes", "ChangedBy", "CreatedAtUtc"
            FROM "WarObjectiveVersions"
            WHERE "FactionId" = @factionId AND "WarId" = @warId
            ORDER BY "Version" DESC
            LIMIT 1
            """;
        AddParameter(command, "factionId", DbType.Int64, factionId);
        AddParameter(command, "warId", DbType.Int64, warId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadVersion(reader) : null;
    }

    private static FactionWarObjectiveVersion ReadVersion(DbDataReader reader)
    {
        var factionId = reader.GetInt64(0);
        var objective = WarObjectiveVersion.Restore(
            reader.GetInt64(1),
            reader.GetInt32(2),
            (WarObjectiveMode)reader.GetInt32(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)));
        return new FactionWarObjectiveVersion(factionId, objective);
    }

    private async Task<T> WithOpenConnectionAsync<T>(
        Func<DbConnection, Task<T>> action,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            return await action(connection);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private void EnsurePostgres()
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("War-objective persistence requires PostgreSQL/Npgsql.");
    }

    private static void ValidateIds(long factionId, long warId)
    {
        if (factionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "Faction id must be positive.");
        if (warId <= 0)
            throw new ArgumentOutOfRangeException(nameof(warId), warId, "War id must be positive.");
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
