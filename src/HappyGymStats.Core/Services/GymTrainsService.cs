using HappyGymStats.Core.Models;
using HappyGymStats.Core.Repositories;

namespace HappyGymStats.Core.Services;

public sealed class GymTrainsService(IUserLogEntryRepository repo)
{
    public Task<CursorPage<GymTrainDto>> GetPageAsync(int take, PageCursor? cursor, CancellationToken ct)
        => repo.GetGymTrainsPageAsync(take, cursor, ct);

    public Task<CursorPage<GymTrainDto>> GetPageAsync(Guid anonymousId, int take, PageCursor? cursor, CancellationToken ct)
        => repo.GetGymTrainsPageAsync(anonymousId, take, cursor, ct);
}
