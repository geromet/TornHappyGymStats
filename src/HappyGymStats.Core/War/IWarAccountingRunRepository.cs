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

public enum WarAccountingRunLifecycleKind
{
    Approved = 1,
    Superseded = 2
}

/// <summary>
/// Append-only lifecycle evidence for a frozen run. Approval and supersession are
/// events rather than mutable flags so actor, time and reason remain auditable.
/// </summary>
public sealed record WarAccountingRunLifecycleEvent(
    Guid EventId,
    Guid RunId,
    WarAccountingRunLifecycleKind Kind,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    Guid? SupersedingRunId);

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

    /// <summary>
    /// Appends the one approval event for a frozen run. Re-approval and approval after
    /// supersession are rejected by the persistence contract.
    /// </summary>
    Task<WarAccountingRunLifecycleEvent> ApproveAsync(
        Guid eventId,
        Guid runId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        CancellationToken ct);

    /// <summary>
    /// Supersedes an approved run with another approved frozen run from the same
    /// faction/war scope. The replacement relationship is immutable and auditable.
    /// </summary>
    Task<WarAccountingRunLifecycleEvent> SupersedeAsync(
        Guid eventId,
        Guid runId,
        Guid supersedingRunId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        CancellationToken ct);

    Task<IReadOnlyList<WarAccountingRunLifecycleEvent>> GetLifecycleAsync(
        Guid runId,
        CancellationToken ct);
}
