namespace HappyGymStats.Core.Models;
public sealed record ModifierProvenanceRow(
    string LogEntryId,
    int Scope,
    int VerificationStatus,
    int? SubjectId,
    int? FactionId,
    int? CompanyId,
    int PlayerId);
