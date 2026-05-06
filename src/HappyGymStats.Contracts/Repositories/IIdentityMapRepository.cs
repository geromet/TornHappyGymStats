using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IIdentityMapRepository
{
    // Stages add. Caller commits via IUnitOfWork.
    Task CreateAsync(IdentityMapEntity entity, CancellationToken ct);

    Task<IdentityMapEntity?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct);

    Task<IdentityMapEntity?> GetByKeycloakSubAsync(string keycloakSub, CancellationToken ct);

    // Links a provisional entry to the given Keycloak sub, clears IsProvisional and ExpiresAtUtc.
    // Returns false if the entry does not exist or is already claimed.
    // Caller commits via IUnitOfWork.
    Task<bool> ClaimProvisionalAsync(Guid anonymousId, string keycloakSub, CancellationToken ct);
}
