namespace HappyGymStats.Data.Entities;

public sealed class RawUserLogEntity
{
    public long Id { get; set; }

    public string LogId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? Title { get; set; }

    public string? Category { get; set; }

    public string RawJson { get; set; } = string.Empty;
}
