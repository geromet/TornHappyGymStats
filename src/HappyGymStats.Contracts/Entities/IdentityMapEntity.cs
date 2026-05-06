namespace HappyGymStats.Data.Entities;

public sealed class IdentityMapEntity
{
    public Guid AnonymousId { get; set; }
    public string? KeycloakSub { get; set; }
    public bool IsProvisional { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
