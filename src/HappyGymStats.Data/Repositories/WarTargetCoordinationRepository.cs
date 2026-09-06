using System.Data;
using System.Data.Common;
using HappyGymStats.Core.War;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class WarTargetCoordinationRepository(HappyGymStatsDbContext db)
{
    public async Task<bool> TryCreateClaimAsync(
        WarTargetClaim claim,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(claim);
        EnsurePostgres();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AcquireScopeLockAsync(connection, transaction, claim.FactionId, claim.WarId, ct);

            if (claim.Mode == WarTargetClaimMode.Primary
                && await HasPrimaryConflictAsync(connection, transaction, claim, nowUtc, ct))
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            await InsertClaimAsync(connection, transaction, claim, ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task SaveReservationAsync(WarTargetReservation reservation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        EnsurePostgres();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AcquireScopeLockAsync(connection, transaction, reservation.FactionId, reservation.WarId, ct);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO "WarTargetReservations" (
                        "Id", "FactionId", "WarId", "TargetMemberId",
                        "ActivatesAtUtc", "ExpiresAtUtc", "PrimaryAttackerMemberId")
                    VALUES (
                        @id, @factionId, @warId, @targetMemberId,
                        @activatesAtUtc, @expiresAtUtc, @primaryAttackerMemberId)
                    """;
                AddParameter(command, "id", DbType.Guid, reservation.Id);
                AddParameter(command, "factionId", DbType.Int64, reservation.FactionId);
                AddParameter(command, "warId", DbType.Int64, reservation.WarId);
                AddParameter(command, "targetMemberId", DbType.Int64, reservation.TargetMemberId);
                AddParameter(command, "activatesAtUtc", DbType.DateTime, reservation.ActivatesAtUtc.UtcDateTime);
                AddParameter(command, "expiresAtUtc", DbType.DateTime, reservation.ExpiresAtUtc.UtcDateTime);
                AddParameter(command, "primaryAttackerMemberId", DbType.Int64, reservation.PrimaryAttackerMemberId);
                await command.ExecuteNonQueryAsync(ct);
            }

            for (var ordinal = 0; ordinal < reservation.CandidateOrder.Count; ordinal++)
            {
                await using var candidate = connection.CreateCommand();
                candidate.Transaction = transaction;
                candidate.CommandText = """
                    INSERT INTO "WarTargetReservationCandidates" (
                        "ReservationId", "Ordinal", "AttackerMemberId")
                    VALUES (@reservationId, @ordinal, @attackerMemberId)
                    """;
                AddParameter(candidate, "reservationId", DbType.Guid, reservation.Id);
                AddParameter(candidate, "ordinal", DbType.Int32, ordinal);
                AddParameter(candidate, "attackerMemberId", DbType.Int64, reservation.CandidateOrder[ordinal]);
                await candidate.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<WarTargetReservation?> GetReservationAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Reservation id must be non-empty.", nameof(id));
        EnsurePostgres();

        return await WithOpenConnectionAsync(async connection =>
        {
            long factionId;
            long warId;
            long targetMemberId;
            DateTimeOffset activatesAtUtc;
            DateTimeOffset expiresAtUtc;
            long primaryAttackerMemberId;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT "FactionId", "WarId", "TargetMemberId", "ActivatesAtUtc",
                           "ExpiresAtUtc", "PrimaryAttackerMemberId"
                    FROM "WarTargetReservations"
                    WHERE "Id" = @id
                    """;
                AddParameter(command, "id", DbType.Guid, id);

                await using var reader = await command.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return null;

                factionId = reader.GetInt64(0);
                warId = reader.GetInt64(1);
                targetMemberId = reader.GetInt64(2);
                activatesAtUtc = ReadUtc(reader, 3);
                expiresAtUtc = ReadUtc(reader, 4);
                primaryAttackerMemberId = reader.GetInt64(5);
            }

            var candidates = new List<long>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT "AttackerMemberId"
                    FROM "WarTargetReservationCandidates"
                    WHERE "ReservationId" = @id
                    ORDER BY "Ordinal" ASC
                    """;
                AddParameter(command, "id", DbType.Guid, id);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    candidates.Add(reader.GetInt64(0));
            }

            if (candidates.Count == 0 || candidates[0] != primaryAttackerMemberId)
                throw new InvalidOperationException("Persisted reservation candidate order is invalid.");

            return new WarTargetReservation(
                id,
                factionId,
                warId,
                targetMemberId,
                activatesAtUtc,
                expiresAtUtc,
                primaryAttackerMemberId,
                candidates.Skip(1));
        }, ct);
    }

    public async Task<IReadOnlyList<WarTargetClaim>> GetClaimsAsync(
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
                SELECT "Id", "TargetMemberId", "AttackerMemberId", "ClaimedAtUtc", "ExpiresAtUtc", "Mode"
                FROM "WarTargetClaims"
                WHERE "FactionId" = @factionId AND "WarId" = @warId
                ORDER BY "ClaimedAtUtc", "Id"
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);

            var claims = new List<WarTargetClaim>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                claims.Add(new WarTargetClaim(
                    reader.GetGuid(0),
                    factionId,
                    warId,
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    ReadUtc(reader, 3),
                    ReadUtc(reader, 4),
                    (WarTargetClaimMode)reader.GetInt32(5)));
            }

            return (IReadOnlyList<WarTargetClaim>)claims;
        }, ct);
    }

    private static async Task<bool> HasPrimaryConflictAsync(
        DbConnection connection,
        DbTransaction transaction,
        WarTargetClaim claim,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM "WarTargetClaims"
            WHERE "FactionId" = @factionId
              AND "WarId" = @warId
              AND "Mode" = @primaryMode
              AND "ClaimedAtUtc" <= @nowUtc
              AND "ExpiresAtUtc" > @nowUtc
              AND ("TargetMemberId" = @targetMemberId OR "AttackerMemberId" = @attackerMemberId)
            LIMIT 1
            """;
        AddParameter(command, "factionId", DbType.Int64, claim.FactionId);
        AddParameter(command, "warId", DbType.Int64, claim.WarId);
        AddParameter(command, "primaryMode", DbType.Int32, (int)WarTargetClaimMode.Primary);
        AddParameter(command, "nowUtc", DbType.DateTime, nowUtc.UtcDateTime);
        AddParameter(command, "targetMemberId", DbType.Int64, claim.TargetMemberId);
        AddParameter(command, "attackerMemberId", DbType.Int64, claim.AttackerMemberId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task InsertClaimAsync(
        DbConnection connection,
        DbTransaction transaction,
        WarTargetClaim claim,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "WarTargetClaims" (
                "Id", "FactionId", "WarId", "TargetMemberId", "AttackerMemberId",
                "ClaimedAtUtc", "ExpiresAtUtc", "Mode")
            VALUES (
                @id, @factionId, @warId, @targetMemberId, @attackerMemberId,
                @claimedAtUtc, @expiresAtUtc, @mode)
            """;
        AddParameter(command, "id", DbType.Guid, claim.Id);
        AddParameter(command, "factionId", DbType.Int64, claim.FactionId);
        AddParameter(command, "warId", DbType.Int64, claim.WarId);
        AddParameter(command, "targetMemberId", DbType.Int64, claim.TargetMemberId);
        AddParameter(command, "attackerMemberId", DbType.Int64, claim.AttackerMemberId);
        AddParameter(command, "claimedAtUtc", DbType.DateTime, claim.ClaimedAtUtc.UtcDateTime);
        AddParameter(command, "expiresAtUtc", DbType.DateTime, claim.ExpiresAtUtc.UtcDateTime);
        AddParameter(command, "mode", DbType.Int32, (int)claim.Mode);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task AcquireScopeLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        long factionId,
        long warId,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
        AddParameter(command, "key", DbType.String, $"war-target-coordination:{factionId}:{warId}");
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<T> WithOpenConnectionAsync<T>(Func<DbConnection, Task<T>> action, CancellationToken ct)
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
            throw new NotSupportedException("War target coordination persistence requires PostgreSQL/Npgsql.");
    }

    private static void ValidateScope(long factionId, long warId)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
    }

    private static DateTimeOffset ReadUtc(DbDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static void AddParameter(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
