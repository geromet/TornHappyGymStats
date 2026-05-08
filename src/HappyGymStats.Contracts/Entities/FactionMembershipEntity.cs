namespace HappyGymStats.Data.Entities;

// Records that a user (MemberAnonymousId) has ever been associated with a faction/company
// (FactionAnonymousId). Populated from affiliation events during import.
// Does not track current vs. past membership — that requires log-type analysis deferred to later.
public sealed class FactionMembershipEntity
{
    public Guid FactionAnonymousId { get; set; }
    public Guid MemberAnonymousId { get; set; }
}
