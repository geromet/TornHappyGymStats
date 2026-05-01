namespace HappyGymStats.Data.Entities;

/// <summary>
/// Persists time-bounded provenance evidence for a derived train reconstruction row.
/// This contract allows downstream reconstruction and API layers to distinguish verified
/// evidence from unresolved personal/faction/company scope gaps.
/// </summary>
public sealed class ModifierProvenanceEntity
{
    public long Id { get; set; }

    /// <summary>
    /// Foreign key to the derived train row this provenance interval qualifies.
    /// </summary>
    public string DerivedGymTrainLogId { get; set; } = string.Empty;

    /// <summary>
    /// Evidence scope (personal, faction, company).
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Subject identifier for the scope owner (required for personal scope).
    /// </summary>
    public string? SubjectId { get; set; }

    /// <summary>
    /// Faction identifier for faction-sourced evidence when applicable.
    /// </summary>
    public string? FactionId { get; set; }

    /// <summary>
    /// Company identifier for company-sourced evidence when applicable.
    /// </summary>
    public string? CompanyId { get; set; }

    public DateTimeOffset ValidFromUtc { get; set; }

    public DateTimeOffset? ValidToUtc { get; set; }

    /// <summary>
    /// Verification lifecycle state (verified, unresolved, unavailable).
    /// </summary>
    public string VerificationStatus { get; set; } = string.Empty;

    /// <summary>
    /// Machine-readable reason for unresolved/unavailable provenance.
    /// </summary>
    public string VerificationReasonCode { get; set; } = string.Empty;

    public string? VerificationDetails { get; set; }

    public DerivedGymTrainEntity? DerivedGymTrain { get; set; }
}
