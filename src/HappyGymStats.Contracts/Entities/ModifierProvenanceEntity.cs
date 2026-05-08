namespace HappyGymStats.Data.Entities;

public sealed class ModifierProvenanceEntity
{
    public Guid AnonymousId { get; set; }
    public string LogEntryId { get; set; } = string.Empty;
    public int Scope { get; set; }  // ModifierScope bitmask: 1=personal, 2=faction, 4=company
    public int? SubjectId { get; set; }
    public int? FactionId { get; set; }
    public int? CompanyId { get; set; }
    public int VerificationStatus { get; set; }  // 1=verified, 2=unresolved, 3=unavailable
}
