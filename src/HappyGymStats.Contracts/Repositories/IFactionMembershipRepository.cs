namespace HappyGymStats.Core.Repositories;

public interface IFactionMembershipRepository
{
    // Records membership if not already present. Stages add. Caller commits via IUnitOfWork.
    Task AddOrIgnoreAsync(Guid factionAnonymousId, Guid memberAnonymousId, CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetMemberAnonymousIdsAsync(Guid factionAnonymousId, CancellationToken ct);
}
