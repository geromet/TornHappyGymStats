namespace HappyGymStats.Core.War;

public sealed record FactionWarObjectiveVersion(
    long FactionId,
    WarObjectiveVersion Objective);

public interface IWarObjectiveRepository
{
    Task<FactionWarObjectiveVersion> AppendNextAsync(
        long factionId,
        long warId,
        WarObjectiveMode mode,
        string changedBy,
        DateTimeOffset createdAtUtc,
        int? stopAtFactionScore,
        string? notes,
        CancellationToken ct);

    Task<FactionWarObjectiveVersion?> GetCurrentAsync(
        long factionId,
        long warId,
        CancellationToken ct);

    Task<IReadOnlyList<FactionWarObjectiveVersion>> GetHistoryAsync(
        long factionId,
        long warId,
        CancellationToken ct);
}
