namespace HappyGymStats.Data.Entities;

public sealed class ImportRunEntity
{
    public long Id { get; set; }
    public Guid? AnonymousId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Outcome { get; set; } = "running";
    public string? ErrorMessage { get; set; }
    public int PagesFetched { get; set; }
    public long LogsFetched { get; set; }
    public long LogsAppended { get; set; }
    public string? NextUrl { get; set; }
}
