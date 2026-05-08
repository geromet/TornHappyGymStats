using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Reconstruction;

public sealed record AffiliationEventRecord(
    DateTimeOffset OccurredAtUtc,
    AffiliationScope Scope,
    int AffiliationId,
    int LogTypeId,
    int? SenderId);
