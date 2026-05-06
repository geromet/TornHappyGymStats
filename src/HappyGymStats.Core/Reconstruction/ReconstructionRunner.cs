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
    private readonly IAffiliationEventRepository _affiliationRepo;
    private readonly IUnitOfWork _unitOfWork;

    public ReconstructionRunner(
        IUserLogEntryRepository userLogRepo,
        IImportRunRepository importRunRepo,
        IModifierProvenanceRepository provenanceRepo,
        IAffiliationEventRepository affiliationRepo,
        IUnitOfWork unitOfWork)
    {
        _userLogRepo = userLogRepo;
        _importRunRepo = importRunRepo;
        _provenanceRepo = provenanceRepo;
        _affiliationRepo = affiliationRepo;
        _unitOfWork = unitOfWork;
    }

    public sealed record RunResult(
        bool Success,
        string? ErrorMessage,
        IReadOnlyList<DerivedGymTrain> DerivedGymTrains,
        ReconstructionStats? Stats,
        DateTimeOffset AnchorTimeUtc);

    public async Task<RunResult> RunAsync(
        Guid anonymousId,
        int currentHappy,
        DateTimeOffset anchorTimeUtc,
        CancellationToken ct = default)
    {
        if (currentHappy < 0)
            throw new ArgumentOutOfRangeException(nameof(currentHappy), currentHappy, "currentHappy must be >= 0.");
        if (anchorTimeUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("anchorTimeUtc must be in UTC (offset +00:00).", nameof(anchorTimeUtc));

        if (anonymousId == Guid.Empty)
            anonymousId = await _importRunRepo.ResolveAnonymousIdAsync(ct) ?? Guid.Empty;

        if (anonymousId == Guid.Empty)
            return new RunResult(
                Success: false,
                ErrorMessage: "Could not resolve anonymousId: no import runs found in the database.",
                DerivedGymTrains: Array.Empty<DerivedGymTrain>(),
                Stats: null,
                AnchorTimeUtc: anchorTimeUtc);

        var records = await _userLogRepo.GetReconstructionRecordsAsync(anonymousId, ct);

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
        await _userLogRepo.StageHappyBeforeTrainBatchAsync(anonymousId, happyUpdates, ct);

        // Stage provenance replacement (delete old + insert new; committed below)
        var affiliationEvents = await _affiliationRepo.GetForPlayerOrderedAsync(anonymousId, ct);
        var provenanceEntities = BuildModifierProvenanceEntities(anonymousId, reconstructed.DerivedGymTrains, affiliationEvents);
        await _provenanceRepo.StageReplacementForPlayerAsync(anonymousId, provenanceEntities, ct);

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

    private static readonly HashSet<int> CompanyLeaveLogTypes = new() { 6260, 6261, 6262 };

    private static List<ModifierProvenanceEntity> BuildModifierProvenanceEntities(
        Guid anonymousId,
        IReadOnlyList<DerivedGymTrain> derivedGymTrains,
        IReadOnlyList<AffiliationEventRecord> affiliationEvents)
    {
        var factionEvents = affiliationEvents.Where(e => e.Scope == Data.Entities.AffiliationScope.Faction).ToList();
        var companyEvents = affiliationEvents.Where(e => e.Scope == Data.Entities.AffiliationScope.Company).ToList();

        var entities = new List<ModifierProvenanceEntity>(derivedGymTrains.Count * 3);

        foreach (var train in derivedGymTrains)
        {
            var trainTime = train.OccurredAtUtc;

            entities.Add(new ModifierProvenanceEntity
            {
                AnonymousId = anonymousId,
                LogEntryId = train.LogId,
                Scope = (int)Data.Entities.ModifierScope.Personal,
                SubjectId = null, // Phase 4: will be EncryptedTornPlayerId in IdentityMap
                FactionId = null,
                CompanyId = null,
                VerificationStatus = (int)Data.Entities.VerificationStatus.Verified,
            });

            var latestFaction = factionEvents.LastOrDefault(e => e.OccurredAtUtc <= trainTime);
            entities.Add(new ModifierProvenanceEntity
            {
                AnonymousId = anonymousId,
                LogEntryId = train.LogId,
                Scope = (int)Data.Entities.ModifierScope.Faction,
                SubjectId = latestFaction?.SenderId,
                FactionId = latestFaction?.AffiliationId,
                CompanyId = null,
                VerificationStatus = latestFaction is not null
                    ? (int)Data.Entities.VerificationStatus.Verified
                    : (int)Data.Entities.VerificationStatus.Unresolved,
            });

            var latestCompany = companyEvents.LastOrDefault(e => e.OccurredAtUtc <= trainTime);
            var activeCompanyId = latestCompany is not null && !CompanyLeaveLogTypes.Contains(latestCompany.LogTypeId)
                ? latestCompany.AffiliationId
                : (int?)null;
            entities.Add(new ModifierProvenanceEntity
            {
                AnonymousId = anonymousId,
                LogEntryId = train.LogId,
                Scope = (int)Data.Entities.ModifierScope.Company,
                SubjectId = activeCompanyId.HasValue ? latestCompany?.SenderId : null,
                FactionId = null,
                CompanyId = activeCompanyId,
                VerificationStatus = activeCompanyId.HasValue
                    ? (int)Data.Entities.VerificationStatus.Verified
                    : (int)Data.Entities.VerificationStatus.Unresolved,
            });
        }

        return entities;
    }
}
