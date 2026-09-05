namespace HappyGymStats.Core.War;

public enum WarObjectiveMode
{
    CompetitiveWin = 0,
    TermedWin = 1,
    TermedLoss = 2,
}

/// <summary>
/// Immutable application-level objective version. Persistence stores complete versions;
/// changing terms creates a new version instead of mutating the prior instance.
/// </summary>
public sealed class WarObjectiveVersion
{
    private WarObjectiveVersion(
        long warId,
        int version,
        WarObjectiveMode mode,
        bool isExplicit,
        int? stopAtFactionScore,
        string? notes,
        string changedBy,
        DateTimeOffset createdAtUtc)
    {
        WarId = warId;
        Version = version;
        Mode = mode;
        IsExplicit = isExplicit;
        StopAtFactionScore = stopAtFactionScore;
        Notes = notes;
        ChangedBy = changedBy;
        CreatedAtUtc = createdAtUtc;
    }

    public long WarId { get; }
    public int Version { get; }
    public WarObjectiveMode Mode { get; }
    public bool IsExplicit { get; }
    public int? StopAtFactionScore { get; }
    public string? Notes { get; }
    public string ChangedBy { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Rehydrates a persisted version through the same validation used for newly-created versions.
    /// </summary>
    public static WarObjectiveVersion Restore(
        long warId,
        int version,
        WarObjectiveMode mode,
        bool isExplicit,
        int? stopAtFactionScore,
        string? notes,
        string changedBy,
        DateTimeOffset createdAtUtc)
        => Create(
            warId,
            version,
            mode,
            isExplicit,
            stopAtFactionScore,
            notes,
            changedBy,
            createdAtUtc);

    public static WarObjectiveVersion CreateDefault(long warId, DateTimeOffset createdAtUtc)
        => Create(
            warId,
            version: 1,
            WarObjectiveMode.CompetitiveWin,
            isExplicit: false,
            stopAtFactionScore: null,
            notes: null,
            changedBy: "system",
            createdAtUtc);

    public WarObjectiveVersion CreateNext(
        WarObjectiveMode mode,
        string changedBy,
        DateTimeOffset createdAtUtc,
        int? stopAtFactionScore = null,
        string? notes = null)
        => Create(
            WarId,
            checked(Version + 1),
            mode,
            isExplicit: true,
            stopAtFactionScore,
            notes,
            changedBy,
            createdAtUtc);

    private static WarObjectiveVersion Create(
        long warId,
        int version,
        WarObjectiveMode mode,
        bool isExplicit,
        int? stopAtFactionScore,
        string? notes,
        string changedBy,
        DateTimeOffset createdAtUtc)
    {
        if (warId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warId), warId, "War id must be positive.");
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Objective version must be positive.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown war objective mode.");
        }

        if (stopAtFactionScore is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stopAtFactionScore), stopAtFactionScore, "Stop score cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(changedBy))
        {
            throw new ArgumentException("Objective versions require an actor.", nameof(changedBy));
        }

        return new WarObjectiveVersion(
            warId,
            version,
            mode,
            isExplicit,
            stopAtFactionScore,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            changedBy.Trim(),
            createdAtUtc.ToUniversalTime());
    }
}

public sealed record WarObjectiveEvaluation(
    bool RecommendationsAllowed,
    string? StopReason);

public static class WarObjectiveEvaluator
{
    public static WarObjectiveEvaluation Evaluate(
        WarObjectiveVersion objective,
        int factionScore)
    {
        ArgumentNullException.ThrowIfNull(objective);

        if (factionScore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factionScore), factionScore, "Faction score cannot be negative.");
        }

        if (objective.StopAtFactionScore is { } stopScore && factionScore >= stopScore)
        {
            return new WarObjectiveEvaluation(
                RecommendationsAllowed: false,
                StopReason: $"Faction-configured stop score {stopScore} reached.");
        }

        return new WarObjectiveEvaluation(RecommendationsAllowed: true, StopReason: null);
    }
}
