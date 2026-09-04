using HappyGymStats.Core.War;

namespace HappyGymStats.Data.Entities;

public sealed class CombatIntelObservationEntity
{
    public string ObservationId { get; set; } = string.Empty;
    public long PlayerId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public DateTimeOffset FetchedAtUtc { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public CombatIntelClassification Classification { get; set; }
    public decimal? Value { get; set; }
    public decimal? LowerBound { get; set; }
    public decimal? UpperBound { get; set; }
    public string? ProviderMetadata { get; set; }
    public CombatIntelVisibilityScope VisibilityScope { get; set; }
    public string? VisibilityOwner { get; set; }
    public string? SupersedesObservationId { get; set; }
    public CombatIntelObservationEntity? SupersedesObservation { get; set; }
}
