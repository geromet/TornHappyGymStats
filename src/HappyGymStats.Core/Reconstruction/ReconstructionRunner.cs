using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Models;
using HappyGymStats.Data.Entities;
using static HappyGymStats.Core.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// End-to-end orchestration: read typed logs from DB → extract events → reconstruct happy values → write derived output.
/// </summary>
public sealed class ReconstructionRunner
{
    private readonly IUserLogEntryRepository _userLogRepo;
    private readonly IImportRunRepository _importRunRepo;
    private readonly IModifierProvenanceRepository _provenanceRepo;
    private readonly IUnitOfWork _unitOfWork;

    public ReconstructionRunner(
        IUserLogEntryRepository userLogRepo,
        IImportRunRepository importRunRepo,
        IModifierProvenanceRepository provenanceRepo,
        IUnitOfWork unitOfWork)
    {
        _userLogRepo = userLogRepo;
        _importRunRepo = importRunRepo;
        _provenanceRepo = provenanceRepo;
        _unitOfWork = unitOfWork;
    }

    public sealed record RunResult(
        bool Success,
        string? ErrorMessage,
        IReadOnlyList<DerivedGymTrain> DerivedGymTrains,
        ReconstructionStats? Stats,
        DateTimeOffset AnchorTimeUtc);

    public async Task<RunResult> RunAsync(
        int playerId,
        int currentHappy,
        DateTimeOffset anchorTimeUtc,
        CancellationToken ct = default)
    {
        // Keep validation from original Run method unchanged:
        if (currentHappy < 0)
            throw new ArgumentOutOfRangeException(nameof(currentHappy), currentHappy, "currentHappy must be >= 0.");
        if (anchorTimeUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("anchorTimeUtc must be in UTC (offset +00:00).", nameof(anchorTimeUtc));

        // Resolve playerId if not provided
        if (playerId == 0)
            playerId = await _importRunRepo.ResolvePlayerIdAsync(ct);

        if (playerId == 0)
            return new RunResult(
                Success: false,
                ErrorMessage: "Could not resolve playerId: no import runs found in the database.",
                DerivedGymTrains: Array.Empty<DerivedGymTrain>(),
                Stats: null,
                AnchorTimeUtc: anchorTimeUtc);

        var records = await _userLogRepo.GetReconstructionRecordsAsync(playerId, ct);

        // Keep existing extraction and reconstruction logic unchanged:
        var extract = LogEventExtractor.Extract(records);
        var events = new List<ReconstructionEvent>();
        foreach (var ev in extract.Events)
        {
            ct.ThrowIfCancellationRequested();
            events.Add(ev);
        }

        var reconstructed = HappyTimelineReconstructor.RunForward(events);

        // Stage happy-before-train updates (batch load + mutate; committed below)
        var happyUpdates = reconstructed.DerivedGymTrains
            .Select(t => new HappyBeforeTrainUpdate(t.LogId, t.HappyBeforeTrain))
            .ToList();
        await _userLogRepo.StageHappyBeforeTrainBatchAsync(playerId, happyUpdates, ct);

        // Stage provenance replacement (delete old + insert new; committed below)
        var provenanceEntities = BuildModifierProvenanceEntities(playerId, reconstructed.DerivedGymTrains);
        await _provenanceRepo.StageReplacementForPlayerAsync(playerId, provenanceEntities, ct);

        // Single atomic commit — replaces the explicit BeginTransaction/Commit
        await _unitOfWork.SaveChangesAsync(ct);

        var stats = new ReconstructionStats(
            GymTrainEventsExtracted: extract.Stats.GymTrainEventsExtracted,
            MaxHappyEventsExtracted: extract.Stats.MaxHappyEventsExtracted,
            HappyDeltaEventsExtracted: extract.Stats.HappyDeltaEventsExtracted,
            GymTrainsDerived: reconstructed.Stats.GymTrainsDerived,
            ClampAppliedCount: reconstructed.Stats.ClampAppliedCount,
            WarningCount: reconstructed.Stats.WarningCount);

        return new RunResult(
            Success: true,
            ErrorMessage: null,
            DerivedGymTrains: reconstructed.DerivedGymTrains,
            Stats: stats,
            AnchorTimeUtc: anchorTimeUtc);
    }

    // Keep BuildModifierProvenanceEntities unchanged — it's pure logic
    private static List<ModifierProvenanceEntity> BuildModifierProvenanceEntities(int playerId, IReadOnlyList<DerivedGymTrain> derivedGymTrains)
    {
        var entities = new List<ModifierProvenanceEntity>(derivedGymTrains.Count * 3);

        foreach (var train in derivedGymTrains)
        {
            // Personal: scope=1 (ModifierScope.Personal), SubjectId=playerId, verified
            entities.Add(new ModifierProvenanceEntity
            {
                PlayerId = playerId,
                LogEntryId = train.LogId,
                Scope = (int)Data.Entities.ModifierScope.Personal,
                SubjectId = playerId,
                FactionId = null,
                CompanyId = null,
                VerificationStatus = (int)Data.Entities.VerificationStatus.Verified,
            });

            // Faction: scope=2 (ModifierScope.Faction), unresolved
            entities.Add(new ModifierProvenanceEntity
            {
                PlayerId = playerId,
                LogEntryId = train.LogId,
                Scope = (int)Data.Entities.ModifierScope.Faction,
                SubjectId = null,
                FactionId = null,
                CompanyId = null,
                VerificationStatus = (int)Data.Entities.VerificationStatus.Unresolved,
            });

            // Company: scope=4 (ModifierScope.Company), unresolved
            entities.Add(new ModifierProvenanceEntity
            {
                PlayerId = playerId,
                LogEntryId = train.LogId,
                Scope = (int)Data.Entities.ModifierScope.Company,
                SubjectId = null,
                FactionId = null,
                CompanyId = null,
                VerificationStatus = (int)Data.Entities.VerificationStatus.Unresolved,
            });
        }

        return entities;
    }
}
