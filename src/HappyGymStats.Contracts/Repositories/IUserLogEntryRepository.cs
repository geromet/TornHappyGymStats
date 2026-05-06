using HappyGymStats.Core.Models;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IUserLogEntryRepository
{
    Task<HashSet<string>> GetExistingLogIdsAsync(Guid anonymousId, CancellationToken ct);

    // Stages add. Caller commits via IUnitOfWork.
    Task AddRangeAsync(IEnumerable<UserLogEntryEntity> entries, CancellationToken ct);

    // Batch-loads entries by LogId, sets HappyBeforeTrain and computes HappyBeforeDelta
    // from the stored HappyBeforeApi value when staging the update. Tracked mutation — caller commits via IUnitOfWork.
    Task StageHappyBeforeTrainBatchAsync(Guid anonymousId, IReadOnlyList<HappyBeforeTrainUpdate> updates, CancellationToken ct);

    Task<IReadOnlyList<ReconstructionLogRecord>> GetReconstructionRecordsAsync(Guid anonymousId, CancellationToken ct);

    Task<IReadOnlyList<GymLogEntry>> GetGymLogEntriesAsync(CancellationToken ct);

    // Returns fully-formed CursorPage with encoded cursor string.
    Task<CursorPage<GymTrainDto>> GetGymTrainsPageAsync(int take, PageCursor? cursor, CancellationToken ct);

    Task<CursorPage<GymTrainDto>> GetGymTrainsPageAsync(Guid anonymousId, int take, PageCursor? cursor, CancellationToken ct);
}
