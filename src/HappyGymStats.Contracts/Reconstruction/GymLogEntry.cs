namespace HappyGymStats.Core.Reconstruction;

public sealed record GymLogEntry(
    string LogId,
    DateTimeOffset OccurredAtUtc,
    int? HappyBeforeTrain,
    double? EnergyUsed,
    double? StrengthBefore, double? StrengthIncreased,
    double? DefenseBefore, double? DefenseIncreased,
    double? SpeedBefore, double? SpeedIncreased,
    double? DexterityBefore, double? DexterityIncreased);
