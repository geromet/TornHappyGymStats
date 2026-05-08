namespace HappyGymStats.Data.Entities;

public sealed class UserLogEntryEntity
{
    public Guid AnonymousId { get; set; }
    public string LogEntryId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public int LogTypeId { get; set; }
    public int? HappyBeforeApi { get; set; }
    public int? HappyBeforeTrain { get; set; }
    public int? HappyBeforeDelta { get; set; }
    public int? HappyUsed { get; set; }
    public int? HappyIncreased { get; set; }
    public int? HappyDecreased { get; set; }
    public double? EnergyUsed { get; set; }
    public double? StrengthBefore { get; set; }
    public double? StrengthIncreased { get; set; }
    public double? DefenseBefore { get; set; }
    public double? DefenseIncreased { get; set; }
    public double? SpeedBefore { get; set; }
    public double? SpeedIncreased { get; set; }
    public double? DexterityBefore { get; set; }
    public double? DexterityIncreased { get; set; }
    public int? MaxHappyBefore { get; set; }
    public int? MaxHappyAfter { get; set; }
    public int? PropertyId { get; set; }
}
