namespace HappyGymStats.Core.Faction;

// Always returns false until real faction log samples are available to design proper verification.
// Replace with a real implementation in a later phase.
public sealed class StubFactionOwnershipVerifier : IFactionOwnershipVerifier
{
    public Task<bool> IsOwnerAsync(Guid callerAnonymousId, Guid factionAnonymousId, CancellationToken ct)
        => Task.FromResult(false);
}
