using System.Data;
using System.Data.Common;
using System.Text.Json;
using HappyGymStats.Core.War;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class ChainOperationsRepository(HappyGymStatsDbContext db) : IChainOperationsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChainOperationsSnapshot?> GetAsync(
        long factionId,
        long warId,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId);
        EnsurePostgres();

        return await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "Revision", "PayloadJson"::text
                FROM "ChainOperationSnapshots"
                WHERE "FactionId" = @factionId
                  AND "WarId" = @warId
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            var revision = reader.GetInt64(0);
            var payload = JsonSerializer.Deserialize<PersistedSnapshot>(reader.GetString(1), JsonOptions)
                ?? throw new InvalidOperationException("Stored chain-operations snapshot is empty.");
            return ToDomain(factionId, warId, revision, payload);
        }, ct);
    }

    public async Task SaveAsync(
        ChainOperationsSnapshot snapshot,
        long expectedRevision,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateScope(snapshot.FactionId, snapshot.WarId);
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        var requiredRevision = checked(expectedRevision + 1);
        if (snapshot.Revision != requiredRevision)
        {
            throw new ArgumentException(
                $"Snapshot revision must be {requiredRevision} when expected revision is {expectedRevision}.",
                nameof(snapshot));
        }

        EnsurePostgres();
        var payload = JsonSerializer.Serialize(FromDomain(snapshot), JsonOptions);
        await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = expectedRevision == 0
                ? """
                    INSERT INTO "ChainOperationSnapshots"
                        ("FactionId", "WarId", "Revision", "PayloadJson", "UpdatedAtUtc")
                    VALUES
                        (@factionId, @warId, @revision, CAST(@payload AS jsonb), @updatedAtUtc)
                    ON CONFLICT ("FactionId", "WarId") DO NOTHING
                    RETURNING "Revision"
                    """
                : """
                    UPDATE "ChainOperationSnapshots"
                    SET "Revision" = @revision,
                        "PayloadJson" = CAST(@payload AS jsonb),
                        "UpdatedAtUtc" = @updatedAtUtc
                    WHERE "FactionId" = @factionId
                      AND "WarId" = @warId
                      AND "Revision" = @expectedRevision
                    RETURNING "Revision"
                    """;
            AddParameter(command, "factionId", DbType.Int64, snapshot.FactionId);
            AddParameter(command, "warId", DbType.Int64, snapshot.WarId);
            AddParameter(command, "revision", DbType.Int64, snapshot.Revision);
            AddParameter(command, "payload", DbType.String, payload);
            AddParameter(command, "updatedAtUtc", DbType.DateTime, DateTime.UtcNow);
            if (expectedRevision > 0)
                AddParameter(command, "expectedRevision", DbType.Int64, expectedRevision);

            var persisted = await command.ExecuteScalarAsync(ct);
            if (persisted is null || persisted is DBNull)
            {
                throw new InvalidOperationException(
                    "Chain operations changed since they were read; stale or replayed write rejected.");
            }

            if (Convert.ToInt64(persisted) != snapshot.Revision)
                throw new InvalidOperationException("PostgreSQL returned an unexpected chain-operations revision.");
        }, ct);
    }

    private void EnsurePostgres()
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Chain-operations persistence requires the PostgreSQL/Npgsql provider.");
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

    private static PersistedSnapshot FromDomain(ChainOperationsSnapshot snapshot) =>
        new(
            snapshot.Shifts.Select(item => new PersistedShift(
                item.Id, item.WatcherId, item.StartsAtUtc, item.EndsAtUtc)).ToArray(),
            snapshot.CheckIns.Select(item => new PersistedShiftEvent(item.ShiftId, item.OccurredAtUtc)).ToArray(),
            snapshot.CheckOuts.Select(item => new PersistedShiftEvent(item.ShiftId, item.OccurredAtUtc)).ToArray(),
            snapshot.Handoffs.Select(item => new PersistedHandoff(
                item.FromShiftId, item.ToShiftId, item.OccurredAtUtc)).ToArray(),
            snapshot.AttackSlots.Select(item => new PersistedAttackSlot(
                item.Id, item.StartsAtUtc, item.PrimaryMemberId, item.OrderedBackupMemberIds.ToArray())).ToArray());

    private static ChainOperationsSnapshot ToDomain(
        long factionId,
        long warId,
        long revision,
        PersistedSnapshot payload) =>
        new(
            factionId,
            warId,
            revision,
            (payload.Shifts ?? []).Select(item => new WatcherShift(
                item.Id, item.WatcherId, item.StartsAtUtc, item.EndsAtUtc)),
            (payload.CheckIns ?? []).Select(item => new WatcherCheckIn(item.ShiftId, item.OccurredAtUtc)),
            (payload.CheckOuts ?? []).Select(item => new WatcherCheckOut(item.ShiftId, item.OccurredAtUtc)),
            (payload.Handoffs ?? []).Select(item => new WatcherHandoff(
                item.FromShiftId, item.ToShiftId, item.OccurredAtUtc)),
            (payload.AttackSlots ?? []).Select(item => new ChainAttackSlot(
                item.Id, item.StartsAtUtc, item.PrimaryMemberId, item.OrderedBackupMemberIds)));

    private static void ValidateScope(long factionId, long warId)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record PersistedSnapshot(
        PersistedShift[]? Shifts,
        PersistedShiftEvent[]? CheckIns,
        PersistedShiftEvent[]? CheckOuts,
        PersistedHandoff[]? Handoffs,
        PersistedAttackSlot[]? AttackSlots);

    private sealed record PersistedShift(
        Guid Id,
        long WatcherId,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc);

    private sealed record PersistedShiftEvent(Guid ShiftId, DateTimeOffset OccurredAtUtc);

    private sealed record PersistedHandoff(
        Guid FromShiftId,
        Guid ToShiftId,
        DateTimeOffset OccurredAtUtc);

    private sealed record PersistedAttackSlot(
        Guid Id,
        DateTimeOffset StartsAtUtc,
        long PrimaryMemberId,
        long[] OrderedBackupMemberIds);
}
