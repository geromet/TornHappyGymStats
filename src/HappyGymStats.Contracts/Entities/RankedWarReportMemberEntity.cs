namespace HappyGymStats.Data.Entities;

public sealed class RankedWarReportMemberEntity
{
    public long WarId { get; set; }
    public long FactionId { get; set; }
    public string FactionName { get; set; } = string.Empty;
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Chain { get; set; }
    public int Attacks { get; set; }
    public string? StatusState { get; set; }
    public DateTimeOffset? StatusUntilUtc { get; set; }
    public bool IsIdleAttacker { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public DateTimeOffset IngestedAtUtc { get; set; }
}
