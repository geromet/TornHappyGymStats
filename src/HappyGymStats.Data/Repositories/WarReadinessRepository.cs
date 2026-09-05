using System.Data;
using System.Data.Common;
using HappyGymStats.Core.War;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class WarReadinessRepository(HappyGymStatsDbContext db) : IWarReadinessRepository
{
    public async Task<WarReadinessDeclaration?> GetAsync(
        long factionId,
        long warId,
        long memberId,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId, memberId);
        EnsurePostgres();

        return await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "FactionId", "WarId", "MemberId", "State", "WindowStartUtc", "WindowEndUtc",
                       "Note", "UpdatedAtUtc", "Revision"
                FROM "WarReadinessDeclarations"
                WHERE "FactionId" = @factionId
                  AND "WarId" = @warId
                  AND "MemberId" = @memberId
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);
            AddParameter(command, "memberId", DbType.Int64, memberId);

            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadDeclaration(reader) : null;
        }, ct);
    }

    public async Task<IReadOnlyList<WarReadinessDeclaration>> GetForWarAsync(
        long factionId,
        long warId,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId, memberId: 1, validateMember: false);
        EnsurePostgres();

        return await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "FactionId", "WarId", "MemberId", "State", "WindowStartUtc", "WindowEndUtc",
                       "Note", "UpdatedAtUtc", "Revision"
                FROM "WarReadinessDeclarations"
                WHERE "FactionId" = @factionId
                  AND "WarId" = @warId
                ORDER BY "MemberId"
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);

            var declarations = new List<WarReadinessDeclaration>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                declarations.Add(ReadDeclaration(reader));

            return (IReadOnlyList<WarReadinessDeclaration>)declarations;
        }, ct);
    }

    public async Task SaveAsync(
        WarReadinessDeclaration declaration,
        long expectedRevision,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ValidateScope(declaration.FactionId, declaration.WarId, declaration.MemberId);
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        var requiredRevision = checked(expectedRevision + 1);
        if (declaration.Revision != requiredRevision)
        {
            throw new ArgumentException(
                $"Declaration revision must be {requiredRevision} when expected revision is {expectedRevision}.",
                nameof(declaration));
        }

        EnsurePostgres();
        await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "WarReadinessDeclarations" (
                    "FactionId", "WarId", "MemberId", "State", "WindowStartUtc", "WindowEndUtc",
                    "Note", "UpdatedAtUtc", "Revision")
                VALUES (
                    @factionId, @warId, @memberId, @state, @windowStartUtc, @windowEndUtc,
                    @note, @updatedAtUtc, @revision)
                ON CONFLICT ("FactionId", "WarId", "MemberId") DO UPDATE SET
                    "State" = EXCLUDED."State",
                    "WindowStartUtc" = EXCLUDED."WindowStartUtc",
                    "WindowEndUtc" = EXCLUDED."WindowEndUtc",
                    "Note" = EXCLUDED."Note",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc",
                    "Revision" = EXCLUDED."Revision"
                WHERE "WarReadinessDeclarations"."Revision" = @expectedRevision
                RETURNING "Revision"
                """;
            AddDeclarationParameters(command, declaration);
            AddParameter(command, "expectedRevision", DbType.Int64, expectedRevision);

            var persisted = await command.ExecuteScalarAsync(ct);
            if (persisted is null || persisted is DBNull)
            {
                throw new InvalidOperationException(
                    "Readiness declaration changed since it was read; stale write rejected.");
            }

            if (Convert.ToInt64(persisted) != declaration.Revision)
                throw new InvalidOperationException("PostgreSQL returned an unexpected readiness revision.");
        }, ct);
    }

    public async Task<bool> ClearAsync(
        long factionId,
        long warId,
        long memberId,
        long expectedRevision,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId, memberId);
        if (expectedRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        EnsurePostgres();

        return await WithOpenConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM "WarReadinessDeclarations"
                WHERE "FactionId" = @factionId
                  AND "WarId" = @warId
                  AND "MemberId" = @memberId
                  AND "Revision" = @expectedRevision
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);
            AddParameter(command, "memberId", DbType.Int64, memberId);
            AddParameter(command, "expectedRevision", DbType.Int64, expectedRevision);
            return await command.ExecuteNonQueryAsync(ct) == 1;
        }, ct);
    }

    private void EnsurePostgres()
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "War-readiness persistence currently requires the PostgreSQL/Npgsql provider.");
        }
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

    private Task WithOpenConnectionAsync(
        Func<DbConnection, Task> action,
        CancellationToken ct) =>
        WithOpenConnectionAsync(async connection =>
        {
            await action(connection);
            return true;
        }, ct);

    private static WarReadinessDeclaration ReadDeclaration(DbDataReader reader) =>
        WarReadinessDeclaration.Create(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            (WarReadinessState)reader.GetInt32(3),
            ToUtcOffset(reader.GetDateTime(4)),
            ToUtcOffset(reader.GetDateTime(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            ToUtcOffset(reader.GetDateTime(7)),
            reader.GetInt64(8));

    private static void AddDeclarationParameters(DbCommand command, WarReadinessDeclaration declaration)
    {
        AddParameter(command, "factionId", DbType.Int64, declaration.FactionId);
        AddParameter(command, "warId", DbType.Int64, declaration.WarId);
        AddParameter(command, "memberId", DbType.Int64, declaration.MemberId);
        AddParameter(command, "state", DbType.Int32, (int)declaration.State);
        AddParameter(command, "windowStartUtc", DbType.DateTime, declaration.WindowStartUtc.UtcDateTime);
        AddParameter(command, "windowEndUtc", DbType.DateTime, declaration.WindowEndUtc.UtcDateTime);
        AddParameter(command, "note", DbType.String, declaration.Note);
        AddParameter(command, "updatedAtUtc", DbType.DateTime, declaration.UpdatedAtUtc.UtcDateTime);
        AddParameter(command, "revision", DbType.Int64, declaration.Revision);
    }

    private static void ValidateScope(long factionId, long warId, long memberId, bool validateMember = true)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        if (validateMember && memberId <= 0) throw new ArgumentOutOfRangeException(nameof(memberId));
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
}
