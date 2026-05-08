namespace HappyGymStats.Data.Entities;

public sealed class IdentityMapEntity
{
    public Guid AnonymousId { get; set; }
    public string? KeycloakSub { get; set; }
    public bool IsProvisional { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    // P-256 SPKI bytes submitted by the client at import time.
    public byte[]? PublicKey { get; set; }

    // ECIES ciphertext of the user's Torn player ID (UTF-8 decimal string bytes).
    // Only set when PublicKey is present.
    public byte[]? EncryptedTornPlayerId { get; set; }
}
