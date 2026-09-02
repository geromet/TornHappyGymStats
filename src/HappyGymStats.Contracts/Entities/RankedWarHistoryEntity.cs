namespace HappyGymStats.Data.Entities;

public sealed class RankedWarHistoryEntity
{
    public long WarId { get; set; }
    public long FactionId { get; set; }
    public string FactionName { get; set; } = string.Empty;
    public long OpponentFactionId { get; set; }
    public string OpponentFactionName { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public long? WinnerFactionId { get; set; }
    public int? FactionScore { get; set; }
    public int? FactionChain { get; set; }
    public int? OpponentScore { get; set; }
    public int? OpponentChain { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public DateTimeOffset IngestedAtUtc { get; set; }
    public DateTimeOffset? ReportCapturedAtUtc { get; set; }
    public DateTimeOffset? ReportIngestedAtUtc { get; set; }
}
