using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IIdentityMapRepository
{
    // Stages add. Caller commits via IUnitOfWork.
    Task CreateAsync(IdentityMapEntity entity, CancellationToken ct);

    Task<IdentityMapEntity?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct);

    Task<IdentityMapEntity?> GetByKeycloakSubAsync(string keycloakSub, CancellationToken ct);

    // Atomically links one unexpired provisional entry to the given Keycloak sub,
    // clears IsProvisional/ExpiresAtUtc, and persists that transition immediately.
    // Returns false if the entry does not exist, is expired/already claimed, or the
    // Keycloak subject concurrently won another identity mapping.
    Task<bool> ClaimProvisionalAsync(Guid anonymousId, string keycloakSub, CancellationToken ct);

    // Stores the ECIES ciphertext of the user's Torn player ID.
    // Caller commits via IUnitOfWork.
    Task StoreEncryptedTornPlayerIdAsync(Guid anonymousId, byte[] encryptedTornPlayerId, CancellationToken ct);

    // Stores (or replaces) the P-256 SPKI public key for a given AnonymousId.
    // Caller commits via IUnitOfWork.
    Task StorePublicKeyAsync(Guid anonymousId, byte[] publicKeySpki, CancellationToken ct);
}
