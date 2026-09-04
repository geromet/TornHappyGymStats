namespace HappyGymStats.Core.War;

/// <summary>
/// Durable append-only storage for provider-neutral combat-intelligence observations.
/// Implementations must preserve observation history and reject invalid supersession chains.
/// </summary>
public interface ICombatIntelRepository
{
    Task AppendAsync(
        CombatIntelObservation observation,
        DateTimeOffset trustedReferenceTimeUtc,
        CancellationToken ct);

    Task<IReadOnlyList<CombatIntelObservation>> GetHistoryAsync(
        long playerId,
        string? provider,
        DateTimeOffset? observedSinceUtc,
        CancellationToken ct);
}
