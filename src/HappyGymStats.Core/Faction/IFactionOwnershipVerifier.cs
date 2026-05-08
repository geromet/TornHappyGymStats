namespace HappyGymStats.Core.Faction;

public interface IFactionOwnershipVerifier
{
    // Returns true only if callerAnonymousId is the verified owner of factionAnonymousId.
    Task<bool> IsOwnerAsync(Guid callerAnonymousId, Guid factionAnonymousId, CancellationToken ct);
}
