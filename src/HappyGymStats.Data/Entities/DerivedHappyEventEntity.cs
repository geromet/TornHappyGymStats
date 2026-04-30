namespace HappyGymStats.Data.Entities;

public sealed class DerivedHappyEventEntity
{
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? SourceLogId { get; set; }

    public int? HappyBeforeEvent { get; set; }

    public int? HappyAfterEvent { get; set; }

    public int? Delta { get; set; }

    public string? Note { get; set; }
}
