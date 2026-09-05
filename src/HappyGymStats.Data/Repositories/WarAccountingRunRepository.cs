using System.Data;
using System.Data.Common;
using HappyGymStats.Core.War;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class WarAccountingRunRepository(HappyGymStatsDbContext db) : IWarAccountingRunRepository
{
    public async Task<FrozenWarAccountingRun> FreezeAsync(
        Guid runId,
        long factionId,
        long warId,
        string frozenBy,
        DateTimeOffset frozenAtUtc,
        CancellationToken ct)
    {
        Validate(runId, factionId, warId, frozenBy);
        EnsurePostgres();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AcquireWarObjectiveLockAsync(connection, transaction, factionId, warId, ct);

            var objectiveVersion = await ReadLatestObjectiveVersionAsync(
                connection,
                transaction,
                factionId,
                warId,
                ct);

            if (objectiveVersion is null)
            {
                await InsertBaselineObjectiveAsync(connection, transaction, factionId, warId, ct);
                objectiveVersion = 1;
            }

            var frozen = new FrozenWarAccountingRun(
                runId,
                factionId,
                warId,
                objectiveVersion.Value,
                frozenBy.Trim(),
                frozenAtUtc.ToUniversalTime());

            await InsertRunAsync(connection, transaction, frozen, ct);
            await transaction.CommitAsync(ct);
            return frozen;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<FrozenWarAccountingRun?> GetAsync(Guid runId, CancellationToken ct)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run id must be non-empty.", nameof(runId));

        EnsurePostgres();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "RunId", "FactionId", "WarId", "ObjectiveVersion", "FrozenBy", "FrozenAtUtc"
                FROM "WarAccountingRuns"
                WHERE "RunId" = @runId
                """;
            AddParameter(command, "runId", DbType.Guid, runId);

            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadRun(reader) : null;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task AcquireWarObjectiveLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        long factionId,
        long warId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
        AddParameter(command, "key", DbType.String, $"war-objective:{factionId}:{warId}");
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int?> ReadLatestObjectiveVersionAsync(
        DbConnection connection,
        DbTransaction transaction,
        long factionId,
        long warId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT "Version"
            FROM "WarObjectiveVersions"
            WHERE "FactionId" = @factionId AND "WarId" = @warId
            ORDER BY "Version" DESC
            LIMIT 1
            """;
        AddParameter(command, "factionId", DbType.Int64, factionId);
        AddParameter(command, "warId", DbType.Int64, warId);

        var value = await command.ExecuteScalarAsync(ct);
        return value is null || value is DBNull ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertBaselineObjectiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        long factionId,
        long warId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "WarObjectiveVersions" (
                "FactionId", "WarId", "Version", "Mode", "IsExplicit",
                "StopAtFactionScore", "Notes", "ChangedBy", "CreatedAtUtc")
            VALUES (
                @factionId, @warId, 1, @mode, FALSE,
                NULL, NULL, 'system', @createdAtUtc)
            """;
        AddParameter(command, "factionId", DbType.Int64, factionId);
        AddParameter(command, "warId", DbType.Int64, warId);
        AddParameter(command, "mode", DbType.Int32, (int)WarObjectiveMode.CompetitiveWin);
        AddParameter(command, "createdAtUtc", DbType.DateTime, DateTimeOffset.UnixEpoch.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRunAsync(
        DbConnection connection,
        DbTransaction transaction,
        FrozenWarAccountingRun run,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "WarAccountingRuns" (
                "RunId", "FactionId", "WarId", "ObjectiveVersion", "FrozenBy", "FrozenAtUtc")
            VALUES (
                @runId, @factionId, @warId, @objectiveVersion, @frozenBy, @frozenAtUtc)
            """;
        AddParameter(command, "runId", DbType.Guid, run.RunId);
        AddParameter(command, "factionId", DbType.Int64, run.FactionId);
        AddParameter(command, "warId", DbType.Int64, run.WarId);
        AddParameter(command, "objectiveVersion", DbType.Int32, run.ObjectiveVersion);
        AddParameter(command, "frozenBy", DbType.String, run.FrozenBy);
        AddParameter(command, "frozenAtUtc", DbType.DateTime, run.FrozenAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static FrozenWarAccountingRun ReadRun(DbDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt32(3),
            reader.GetString(4),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));

    private void EnsurePostgres()
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("War accounting persistence requires PostgreSQL/Npgsql.");
    }

    private static void Validate(Guid runId, long factionId, long warId, string frozenBy)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run id must be non-empty.", nameof(runId));
        if (factionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "Faction id must be positive.");
        if (warId <= 0)
            throw new ArgumentOutOfRangeException(nameof(warId), warId, "War id must be positive.");
        if (string.IsNullOrWhiteSpace(frozenBy))
            throw new ArgumentException("Frozen-by identity must be non-empty.", nameof(frozenBy));
        if (frozenBy.Trim().Length > 200)
            throw new ArgumentOutOfRangeException(nameof(frozenBy), "Frozen-by identity cannot exceed 200 characters.");
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
