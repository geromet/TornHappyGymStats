using System.Data;
using System.Data.Common;
using HappyGymStats.Core.War;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class WarPayoutRepository(HappyGymStatsDbContext db) : IWarPayoutRepository
{
    public async Task<StoredWarPayoutPolicy> AppendPolicyAsync(
        long factionId,
        long warId,
        decimal scoreRate,
        decimal chainRate,
        decimal attackRate,
        decimal fixedMemberAmount,
        string changedBy,
        DateTimeOffset createdAtUtc,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId);
        var actor = NormalizeActor(changedBy);
        new WarPayoutPolicy(1, scoreRate, chainRate, attackRate, fixedMemberAmount).Validate();
        EnsurePostgres();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AcquirePolicyLockAsync(connection, transaction, factionId, warId, ct);
            var nextVersion = checked((await ReadLatestPolicyVersionAsync(connection, transaction, factionId, warId, ct) ?? 0) + 1);
            var policy = new WarPayoutPolicy(nextVersion, scoreRate, chainRate, attackRate, fixedMemberAmount).Validate();
            var stored = new StoredWarPayoutPolicy(factionId, warId, policy, actor, createdAtUtc.ToUniversalTime());

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO "WarPayoutPolicyVersions" (
                    "FactionId", "WarId", "Version", "ScoreRate", "ChainRate", "AttackRate",
                    "FixedMemberAmount", "ChangedBy", "CreatedAtUtc")
                VALUES (
                    @factionId, @warId, @version, @scoreRate, @chainRate, @attackRate,
                    @fixedMemberAmount, @changedBy, @createdAtUtc)
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);
            AddParameter(command, "version", DbType.Int32, nextVersion);
            AddParameter(command, "scoreRate", DbType.Decimal, scoreRate);
            AddParameter(command, "chainRate", DbType.Decimal, chainRate);
            AddParameter(command, "attackRate", DbType.Decimal, attackRate);
            AddParameter(command, "fixedMemberAmount", DbType.Decimal, fixedMemberAmount);
            AddParameter(command, "changedBy", DbType.String, actor);
            AddParameter(command, "createdAtUtc", DbType.DateTime, stored.CreatedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return stored;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<StoredWarPayoutPolicy?> GetPolicyAsync(
        long factionId,
        long warId,
        int version,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId);
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Policy version must be positive.");
        EnsurePostgres();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "FactionId", "WarId", "Version", "ScoreRate", "ChainRate", "AttackRate",
                       "FixedMemberAmount", "ChangedBy", "CreatedAtUtc"
                FROM "WarPayoutPolicyVersions"
                WHERE "FactionId" = @factionId AND "WarId" = @warId AND "Version" = @version
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);
            AddParameter(command, "version", DbType.Int32, version);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadPolicy(reader) : null;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<IReadOnlyList<StoredWarPayoutPolicy>> GetPolicyHistoryAsync(
        long factionId,
        long warId,
        CancellationToken ct)
    {
        ValidateScope(factionId, warId);
        EnsurePostgres();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "FactionId", "WarId", "Version", "ScoreRate", "ChainRate", "AttackRate",
                       "FixedMemberAmount", "ChangedBy", "CreatedAtUtc"
                FROM "WarPayoutPolicyVersions"
                WHERE "FactionId" = @factionId AND "WarId" = @warId
                ORDER BY "Version"
                """;
            AddParameter(command, "factionId", DbType.Int64, factionId);
            AddParameter(command, "warId", DbType.Int64, warId);

            var result = new List<StoredWarPayoutPolicy>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(ReadPolicy(reader));
            return result.AsReadOnly();
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<FrozenWarPayoutResult> CalculateAndFreezeAsync(
        Guid runId,
        int policyVersion,
        decimal poolAmount,
        string calculatedBy,
        DateTimeOffset calculatedAtUtc,
        CancellationToken ct)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run id must be non-empty.", nameof(runId));
        if (policyVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(policyVersion), policyVersion, "Policy version must be positive.");
        WarPayoutPolicy.ValidateAmount(poolAmount, nameof(poolAmount));
        var actor = NormalizeActor(calculatedBy);
        EnsurePostgres();

        // Run/source/policy records are append-only, so this pre-read remains stable while
        // the result transaction persists the deterministic calculation.
        var runRepository = new WarAccountingRunRepository(db);
        var run = await runRepository.GetAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Accounting run {runId:D} was not found.");
        var source = await runRepository.GetSourceAsync(run.SourceSnapshotId, ct)
            ?? throw new InvalidDataException("Frozen accounting source was not found.");
        var storedPolicy = await GetPolicyAsync(run.FactionId, run.WarId, policyVersion, ct)
            ?? throw new KeyNotFoundException($"Payout policy version {policyVersion} was not found for the run scope.");

        var reconciliation = WarPayoutCalculator.Calculate(source, storedPolicy.Policy, poolAmount);
        var frozen = new FrozenWarPayoutResult(
            run.RunId,
            run.FactionId,
            run.WarId,
            run.SourceSnapshotId,
            policyVersion,
            reconciliation.PoolAmount,
            reconciliation.AllocatedAmount,
            reconciliation.UnattributedResidual,
            actor,
            calculatedAtUtc.ToUniversalTime(),
            reconciliation.Lines);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AcquireRunLockAsync(connection, transaction, runId, ct);
            await InsertReconciliationAsync(connection, transaction, frozen, ct);
            foreach (var line in frozen.Lines)
                await InsertLineAsync(connection, transaction, frozen, line, ct);
            await transaction.CommitAsync(ct);
            return frozen;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<FrozenWarPayoutResult?> GetFrozenAsync(Guid runId, CancellationToken ct)
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
            long factionId;
            long warId;
            Guid sourceSnapshotId;
            int policyVersion;
            decimal poolAmount;
            decimal allocatedAmount;
            decimal residual;
            string calculatedBy;
            DateTimeOffset calculatedAtUtc;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT "FactionId", "WarId", "SourceSnapshotId", "PolicyVersion", "PoolAmount",
                           "AllocatedAmount", "UnattributedResidual", "CalculatedBy", "CalculatedAtUtc"
                    FROM "WarPayoutReconciliations"
                    WHERE "RunId" = @runId
                    """;
                AddParameter(command, "runId", DbType.Guid, runId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return null;

                factionId = reader.GetInt64(0);
                warId = reader.GetInt64(1);
                sourceSnapshotId = reader.GetGuid(2);
                policyVersion = reader.GetInt32(3);
                poolAmount = reader.GetDecimal(4);
                allocatedAmount = reader.GetDecimal(5);
                residual = reader.GetDecimal(6);
                calculatedBy = reader.GetString(7);
                calculatedAtUtc = ReadUtc(reader, 8);
            }

            var lines = new List<WarPayoutLine>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT "MemberId", "MemberName", "Score", "Chain", "Attacks", "ScoreAmount",
                           "ChainAmount", "AttackAmount", "FixedAmount", "TotalAmount"
                    FROM "WarPayoutLines"
                    WHERE "RunId" = @runId
                    ORDER BY "MemberId"
                    """;
                AddParameter(command, "runId", DbType.Guid, runId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    lines.Add(new WarPayoutLine(
                        reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                        reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9)));
                }
            }

            return new FrozenWarPayoutResult(
                runId, factionId, warId, sourceSnapshotId, policyVersion, poolAmount,
                allocatedAmount, residual, calculatedBy, calculatedAtUtc, lines.AsReadOnly());
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task InsertReconciliationAsync(
        DbConnection connection,
        DbTransaction transaction,
        FrozenWarPayoutResult frozen,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "WarPayoutReconciliations" (
                "RunId", "FactionId", "WarId", "SourceSnapshotId", "PolicyVersion", "PoolAmount",
                "AllocatedAmount", "UnattributedResidual", "CalculatedBy", "CalculatedAtUtc")
            VALUES (
                @runId, @factionId, @warId, @sourceSnapshotId, @policyVersion, @poolAmount,
                @allocatedAmount, @residual, @calculatedBy, @calculatedAtUtc)
            """;
        AddParameter(command, "runId", DbType.Guid, frozen.RunId);
        AddParameter(command, "factionId", DbType.Int64, frozen.FactionId);
        AddParameter(command, "warId", DbType.Int64, frozen.WarId);
        AddParameter(command, "sourceSnapshotId", DbType.Guid, frozen.SourceSnapshotId);
        AddParameter(command, "policyVersion", DbType.Int32, frozen.PolicyVersion);
        AddParameter(command, "poolAmount", DbType.Decimal, frozen.PoolAmount);
        AddParameter(command, "allocatedAmount", DbType.Decimal, frozen.AllocatedAmount);
        AddParameter(command, "residual", DbType.Decimal, frozen.UnattributedResidual);
        AddParameter(command, "calculatedBy", DbType.String, frozen.CalculatedBy);
        AddParameter(command, "calculatedAtUtc", DbType.DateTime, frozen.CalculatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertLineAsync(
        DbConnection connection,
        DbTransaction transaction,
        FrozenWarPayoutResult frozen,
        WarPayoutLine line,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "WarPayoutLines" (
                "RunId", "SourceSnapshotId", "FactionId", "WarId", "MemberId", "MemberName",
                "Score", "Chain", "Attacks", "ScoreAmount", "ChainAmount", "AttackAmount", "FixedAmount", "TotalAmount")
            VALUES (
                @runId, @sourceSnapshotId, @factionId, @warId, @memberId, @memberName,
                @score, @chain, @attacks, @scoreAmount, @chainAmount, @attackAmount, @fixedAmount, @totalAmount)
            """;
        AddParameter(command, "runId", DbType.Guid, frozen.RunId);
        AddParameter(command, "sourceSnapshotId", DbType.Guid, frozen.SourceSnapshotId);
        AddParameter(command, "factionId", DbType.Int64, frozen.FactionId);
        AddParameter(command, "warId", DbType.Int64, frozen.WarId);
        AddParameter(command, "memberId", DbType.Int64, line.MemberId);
        AddParameter(command, "memberName", DbType.String, line.MemberName);
        AddParameter(command, "score", DbType.Int32, line.Score);
        AddParameter(command, "chain", DbType.Int32, line.Chain);
        AddParameter(command, "attacks", DbType.Int32, line.Attacks);
        AddParameter(command, "scoreAmount", DbType.Decimal, line.ScoreAmount);
        AddParameter(command, "chainAmount", DbType.Decimal, line.ChainAmount);
        AddParameter(command, "attackAmount", DbType.Decimal, line.AttackAmount);
        AddParameter(command, "fixedAmount", DbType.Decimal, line.FixedAmount);
        AddParameter(command, "totalAmount", DbType.Decimal, line.TotalAmount);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int?> ReadLatestPolicyVersionAsync(
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
            FROM "WarPayoutPolicyVersions"
            WHERE "FactionId" = @factionId AND "WarId" = @warId
            ORDER BY "Version" DESC
            LIMIT 1
            """;
        AddParameter(command, "factionId", DbType.Int64, factionId);
        AddParameter(command, "warId", DbType.Int64, warId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null || value is DBNull ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task AcquirePolicyLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        long factionId,
        long warId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
        AddParameter(command, "key", DbType.String, $"war-payout-policy:{factionId}:{warId}");
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task AcquireRunLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid runId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
        AddParameter(command, "key", DbType.String, $"war-payout-result:{runId:D}");
        await command.ExecuteNonQueryAsync(ct);
    }

    private static StoredWarPayoutPolicy ReadPolicy(DbDataReader reader)
    {
        var policy = new WarPayoutPolicy(
            reader.GetInt32(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6)).Validate();
        return new StoredWarPayoutPolicy(reader.GetInt64(0), reader.GetInt64(1), policy, reader.GetString(7), ReadUtc(reader, 8));
    }

    private static DateTimeOffset ReadUtc(DbDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static void ValidateScope(long factionId, long warId)
    {
        if (factionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "Faction id must be positive.");
        if (warId <= 0)
            throw new ArgumentOutOfRangeException(nameof(warId), warId, "War id must be positive.");
    }

    private static string NormalizeActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor must be non-empty.", nameof(actor));
        var normalized = actor.Trim();
        if (normalized.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(actor), "Actor cannot exceed 200 characters.");
        return normalized;
    }

    private void EnsurePostgres()
    {
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            throw new NotSupportedException("War payout persistence requires PostgreSQL.");
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
