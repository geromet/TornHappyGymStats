namespace HappyGymStats.Core.Models;
// HappyBeforeTrain comes from reconstruction. Delta is computed by the repository
// from the stored HappyBeforeApi value when staging the update.
public sealed record HappyBeforeTrainUpdate(string LogId, int? HappyBeforeTrain);
