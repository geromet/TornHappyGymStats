namespace HappyGymStats.Core.War;

/// <summary>
/// Minimal immutable audit binding for a frozen accounting/payout run.
/// The objective tuple identifies the exact durable objective version used by the run;
/// richer ledger/policy/line persistence is layered onto this boundary by #88.
/// </summary>
public sealed record FrozenWarAccountingRun(
    Guid RunId,
    long FactionId,
    long WarId,
    int ObjectiveVersion,
    string FrozenBy,
    DateTimeOffset FrozenAtUtc);

public interface IWarAccountingRunRepository
{
    /// <summary>
    /// Freezes the currently effective objective and persists an immutable run binding.
    /// If no objective has been configured yet, the canonical baseline version 1 is
    /// materialized first. Objective selection is serialized with objective appends.
    /// </summary>
    Task<FrozenWarAccountingRun> FreezeAsync(
        Guid runId,
        long factionId,
        long warId,
        string frozenBy,
        DateTimeOffset frozenAtUtc,
        CancellationToken ct);

    Task<FrozenWarAccountingRun?> GetAsync(Guid runId, CancellationToken ct);
}
