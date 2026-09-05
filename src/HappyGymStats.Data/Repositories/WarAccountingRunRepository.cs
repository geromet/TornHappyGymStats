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
        ValidateRunId(runId);
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

    public Task<WarAccountingRunLifecycleEvent> ApproveAsync(
        Guid eventId,
        Guid runId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        CancellationToken ct)
        => AppendLifecycleAsync(
            new WarAccountingRunLifecycleEvent(
                eventId,
                runId,
                WarAccountingRunLifecycleKind.Approved,
                NormalizeActor(actor),
                occurredAtUtc.ToUniversalTime(),
                NormalizeReason(reason),
                null),
            ct);

    public Task<WarAccountingRunLifecycleEvent> SupersedeAsync(
        Guid eventId,
        Guid runId,
        Guid supersedingRunId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        CancellationToken ct)
    {
        ValidateRunId(supersedingRunId, nameof(supersedingRunId));
        if (supersedingRunId == runId)
            throw new ArgumentException("A run cannot supersede itself.", nameof(supersedingRunId));

        return AppendLifecycleAsync(
            new WarAccountingRunLifecycleEvent(
                eventId,
                runId,
                WarAccountingRunLifecycleKind.Superseded,
                NormalizeActor(actor),
                occurredAtUtc.ToUniversalTime(),
                NormalizeReason(reason),
                supersedingRunId),
            ct);
    }

    public async Task<IReadOnlyList<WarAccountingRunLifecycleEvent>> GetLifecycleAsync(
        Guid runId,
        CancellationToken ct)
    {
        ValidateRunId(runId);
        EnsurePostgres();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "EventId", "RunId", "Kind", "Actor", "OccurredAtUtc", "Reason", "SupersedingRunId"
                FROM "WarAccountingRunLifecycleEvents"
                WHERE "RunId" = @runId
                ORDER BY "OccurredAtUtc", "EventId"
                """;
            AddParameter(command, "runId", DbType.Guid, runId);

            var result = new List<WarAccountingRunLifecycleEvent>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(ReadLifecycleEvent(reader));
            return result;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task<WarAccountingRunLifecycleEvent> AppendLifecycleAsync(
        WarAccountingRunLifecycleEvent lifecycleEvent,
        CancellationToken ct)
    {
        ValidateLifecycleEvent(lifecycleEvent);
        EnsurePostgres();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AcquireRunLifecycleLockAsync(connection, transaction, lifecycleEvent.RunId, ct);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO "WarAccountingRunLifecycleEvents" (
                    "EventId", "RunId", "Kind", "Actor", "OccurredAtUtc", "Reason", "SupersedingRunId")
                VALUES (
                    @eventId, @runId, @kind, @actor, @occurredAtUtc, @reason, @supersedingRunId)
                """;
            AddParameter(command, "eventId", DbType.Guid, lifecycleEvent.EventId);
            AddParameter(command, "runId", DbType.Guid, lifecycleEvent.RunId);
            AddParameter(command, "kind", DbType.Int32, (int)lifecycleEvent.Kind);
            AddParameter(command, "actor", DbType.String, lifecycleEvent.Actor);
            AddParameter(command, "occurredAtUtc", DbType.DateTime, lifecycleEvent.OccurredAtUtc.UtcDateTime);
            AddParameter(command, "reason", DbType.String, lifecycleEvent.Reason);
            AddParameter(command, "supersedingRunId", DbType.Guid, lifecycleEvent.SupersedingRunId);
            await command.ExecuteNonQueryAsync(ct);

            await transaction.CommitAsync(ct);
            return lifecycleEvent;
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

    private static async Task AcquireRunLifecycleLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid runId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
        AddParameter(command, "key", DbType.String, $"war-accounting-run:{runId:D}");
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

    private static WarAccountingRunLifecycleEvent ReadLifecycleEvent(DbDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            (WarAccountingRunLifecycleKind)reader.GetInt32(2),
            reader.GetString(3),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6));

    private void EnsurePostgres()
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("War accounting persistence requires PostgreSQL/Npgsql.");
    }

    private static void Validate(Guid runId, long factionId, long warId, string frozenBy)
    {
        ValidateRunId(runId);
        if (factionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "Faction id must be positive.");
        if (warId <= 0)
            throw new ArgumentOutOfRangeException(nameof(warId), warId, "War id must be positive.");
        if (string.IsNullOrWhiteSpace(frozenBy))
            throw new ArgumentException("Frozen-by identity must be non-empty.", nameof(frozenBy));
        if (frozenBy.Trim().Length > 200)
            throw new ArgumentOutOfRangeException(nameof(frozenBy), "Frozen-by identity cannot exceed 200 characters.");
    }

    private static void ValidateLifecycleEvent(WarAccountingRunLifecycleEvent lifecycleEvent)
    {
        ValidateRunId(lifecycleEvent.EventId, nameof(lifecycleEvent.EventId));
        ValidateRunId(lifecycleEvent.RunId);
        if (!Enum.IsDefined(lifecycleEvent.Kind))
            throw new ArgumentOutOfRangeException(nameof(lifecycleEvent.Kind), lifecycleEvent.Kind, "Lifecycle kind is undefined.");
        if (lifecycleEvent.Actor.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(lifecycleEvent.Actor), "Actor identity cannot exceed 200 characters.");
        if (lifecycleEvent.Reason.Length > 2000)
            throw new ArgumentOutOfRangeException(nameof(lifecycleEvent.Reason), "Reason cannot exceed 2000 characters.");
    }

    private static void ValidateRunId(Guid runId, string paramName = "runId")
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run id must be non-empty.", paramName);
    }

    private static string NormalizeActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor identity must be non-empty.", nameof(actor));
        var normalized = actor.Trim();
        if (normalized.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(actor), "Actor identity cannot exceed 200 characters.");
        return normalized;
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Lifecycle reason must be non-empty.", nameof(reason));
        var normalized = reason.Trim();
        if (normalized.Length > 2000)
            throw new ArgumentOutOfRangeException(nameof(reason), "Lifecycle reason cannot exceed 2000 characters.");
        return normalized;
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
