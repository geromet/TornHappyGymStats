namespace HappyGymStats.Core.War;

public sealed record FactionWarObjectiveVersion(
    long FactionId,
    WarObjectiveVersion Objective);

public interface IWarObjectiveRepository
{
    Task<FactionWarObjectiveVersion> AppendNextAsync(
        long factionId,
        long warId,
        WarObjectiveMode mode,
        string changedBy,
        DateTimeOffset createdAtUtc,
        int? stopAtFactionScore,
        string? notes,
        CancellationToken ct);

    Task<FactionWarObjectiveVersion?> GetCurrentAsync(
        long factionId,
        long warId,
        CancellationToken ct);

    /// <summary>
    /// Resolves the objective consumers should act on. Unconfigured wars receive the
    /// deterministic non-explicit competitive default instead of forcing each consumer
    /// to invent its own fallback semantics.
    /// </summary>
    Task<FactionWarObjectiveVersion> GetEffectiveAsync(
        long factionId,
        long warId,
        CancellationToken ct);

    /// <summary>
    /// Returns the effective objective as a durable, immutable version suitable for
    /// audit/freeze boundaries such as payout accounting. If the war is still
    /// unconfigured, materializes the canonical version-1 competitive baseline before
    /// returning it. The operation is serialized with explicit objective appends so a
    /// caller can safely persist the returned (faction, war, version) tuple.
    /// </summary>
    Task<FactionWarObjectiveVersion> GetDurableEffectiveAsync(
        long factionId,
        long warId,
        CancellationToken ct);

    Task<IReadOnlyList<FactionWarObjectiveVersion>> GetHistoryAsync(
        long factionId,
        long warId,
        CancellationToken ct);
}
