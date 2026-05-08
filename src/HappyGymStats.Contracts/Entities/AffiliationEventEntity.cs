namespace HappyGymStats.Data.Entities;

public sealed class AffiliationEventEntity
{
    public Guid AnonymousId { get; set; }
    public string SourceLogEntryId { get; set; } = string.Empty;
    public int LogTypeId { get; set; }
    public AffiliationScope Scope { get; set; }
    public int AffiliationId { get; set; }
    public int? SenderId { get; set; }
    public int? PositionBefore { get; set; }
    public int? PositionAfter { get; set; }

    // ECIES ciphertext of AffiliationId (UTF-8 decimal string bytes).
    // Null when the user has not submitted a public key.
    public byte[]? EncryptedAffiliationId { get; set; }
}
