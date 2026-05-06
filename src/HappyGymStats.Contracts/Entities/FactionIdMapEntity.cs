namespace HappyGymStats.Data.Entities;

// Maps a plaintext Torn affiliation ID (faction or company) to a stable AnonymousId GUID.
// Created on first encounter during import; immutable thereafter.
public sealed class FactionIdMapEntity
{
    public int AffiliationId { get; set; }
    public AffiliationScope Scope { get; set; }
    public Guid FactionAnonymousId { get; set; }
}
