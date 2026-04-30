using HappyGymStats.Storage;

using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Reconstruction;

/// <summary>
/// End-to-end orchestration: read raw JSONL → extract events → reconstruct happy values → write derived output.
/// </summary>
public sealed class ReconstructionRunner
{
    private readonly AppPaths _paths;

    public ReconstructionRunner(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public sealed record RunResult(
        bool Success,
        string? ErrorMessage,
        string DerivedOutputPath,
        IReadOnlyList<DerivedGymTrain> DerivedGymTrains,
        ReconstructionStats? Stats,
        DateTimeOffset AnchorTimeUtc);

    public RunResult Run(
        int currentHappy,
        DateTimeOffset anchorTimeUtc,
        CancellationToken ct = default)
    {
        if (currentHappy < 0)
            throw new ArgumentOutOfRangeException(nameof(currentHappy), currentHappy, "currentHappy must be >= 0.");

        if (anchorTimeUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("anchorTimeUtc must be in UTC (offset +00:00).", nameof(anchorTimeUtc));

        Directory.CreateDirectory(_paths.DataDirectory);
        Directory.CreateDirectory(_paths.DerivedDirectory);

        var read = JsonlLogReader.Read(_paths.LogsJsonlPath);
        if (!read.Success)
        {
            return new RunResult(
                Success: false,
                ErrorMessage: read.ErrorMessage,
                DerivedOutputPath: _paths.DerivedGymTrainsJsonlPath,
                DerivedGymTrains: Array.Empty<DerivedGymTrain>(),
                Stats: null,
                AnchorTimeUtc: anchorTimeUtc);
        }

        // Extract events (minimal representation) and detach from raw JSON as early as possible.
        var extract = LogEventExtractor.Extract(read.Records);
        var events = new List<ReconstructionEvent>();

        foreach (var ev in extract.Events)
        {
            ct.ThrowIfCancellationRequested();
            events.Add(ev);
        }

        // Core reconstruction (forward, anchor-driven).
        // NOTE: currentHappy/anchorTimeUtc are currently used only by the UI/CLI contract.
        // Forward reconstruction derives what it can from anchors inside the log stream.
        var reconstructed = HappyTimelineReconstructor.RunForward(events);

        // Persist derived sidecars (atomic write). If persistence fails, do not pretend the run succeeded.
        var writeTrains = DerivedGymTrainStore.WriteAllAtomic(_paths.DerivedGymTrainsJsonlPath, reconstructed.DerivedGymTrains);
        if (!writeTrains.Success)
        {
            return new RunResult(
                Success: false,
                ErrorMessage: writeTrains.ErrorMessage,
                DerivedOutputPath: _paths.DerivedGymTrainsJsonlPath,
                DerivedGymTrains: reconstructed.DerivedGymTrains,
                Stats: null,
                AnchorTimeUtc: anchorTimeUtc);
        }

        var writeEvents = DerivedHappyEventStore.WriteAllAtomic(_paths.DerivedHappyEventsJsonlPath, reconstructed.DerivedHappyEvents);
        if (!writeEvents.Success)
        {
            return new RunResult(
                Success: false,
                ErrorMessage: writeEvents.ErrorMessage,
                DerivedOutputPath: _paths.DerivedGymTrainsJsonlPath,
                DerivedGymTrains: reconstructed.DerivedGymTrains,
                Stats: null,
                AnchorTimeUtc: anchorTimeUtc);
        }

        var stats = new ReconstructionStats(
            LinesRead: read.Stats.LinesRead,
            MalformedLines: read.Stats.MalformedLines,
            GymTrainEventsExtracted: extract.Stats.GymTrainEventsExtracted,
            MaxHappyEventsExtracted: extract.Stats.MaxHappyEventsExtracted,
            HappyDeltaEventsExtracted: extract.Stats.HappyDeltaEventsExtracted,
            GymTrainsDerived: reconstructed.Stats.GymTrainsDerived,
            ClampAppliedCount: reconstructed.Stats.ClampAppliedCount,
            WarningCount: reconstructed.Stats.WarningCount);

        return new RunResult(
            Success: true,
            ErrorMessage: null,
            DerivedOutputPath: _paths.DerivedGymTrainsJsonlPath,
            DerivedGymTrains: reconstructed.DerivedGymTrains,
            Stats: stats,
            AnchorTimeUtc: anchorTimeUtc);
    }
}
