namespace HappyGymStats.Core.Models;

public sealed record FactionMemberSummaryDto(
    Guid AnonymousId,
    int TrainCount,
    DateTimeOffset? LastTrainAtUtc);
