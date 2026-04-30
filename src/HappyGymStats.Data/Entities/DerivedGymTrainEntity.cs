namespace HappyGymStats.Data.Entities;

public sealed class DerivedGymTrainEntity
{
    public string LogId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public int HappyBeforeTrain { get; set; }

    public int HappyAfterTrain { get; set; }

    public int HappyUsed { get; set; }

    public long RegenTicksApplied { get; set; }

    public int RegenHappyGained { get; set; }

    public int? MaxHappyAtTimeUtc { get; set; }

    public bool ClampedToMax { get; set; }
}
