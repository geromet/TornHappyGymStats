namespace HappyGymStats.Core.War;

/// <summary>
/// Durable storage for the current member-authored readiness declaration in one faction/war scope.
/// Callers must apply <see cref="WarReadinessMutationPolicy"/> before writes; persistence adds
/// optimistic revision integrity so stale writers cannot overwrite a newer member declaration.
/// </summary>
public interface IWarReadinessRepository
{
    Task<WarReadinessDeclaration?> GetAsync(
        long factionId,
        long warId,
        long memberId,
        CancellationToken ct);

    Task<IReadOnlyList<WarReadinessDeclaration>> GetForWarAsync(
        long factionId,
        long warId,
        CancellationToken ct);

    Task SaveAsync(
        WarReadinessDeclaration declaration,
        long expectedRevision,
        CancellationToken ct);

    Task<bool> ClearAsync(
        long factionId,
        long warId,
        long memberId,
        long expectedRevision,
        CancellationToken ct);
}
