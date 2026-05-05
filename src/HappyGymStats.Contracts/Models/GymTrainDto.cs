namespace HappyGymStats.Core.Models;
public sealed record GymTrainDto(
    string LogId,
    DateTimeOffset OccurredAtUtc,
    int? HappyBeforeTrain,
    int? HappyAfterTrain,
    int? HappyUsed,
    int? MaxHappyBefore,
    int? MaxHappyAfter);
