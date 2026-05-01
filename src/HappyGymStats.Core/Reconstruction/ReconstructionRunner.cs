using HappyGymStats.Core.Storage;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Data.Storage;
using Microsoft.EntityFrameworkCore;
using static HappyGymStats.Core.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// End-to-end orchestration: read raw logs → extract events → reconstruct happy values → write derived output.
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

        var databasePath = SqlitePaths.ResolveDatabasePath(_paths.DataDirectory);
        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        using var db = new HappyGymStatsDbContext(dbOptions);
        db.Database.Migrate();

        var preferDatabase = SqlitePaths.ShouldPreferDatabase(databasePath, _paths.LogsJsonlPath);

        IReadOnlyList<ReconstructionLogRecord> records;
        JsonlLogReader.ReadStats? readerStats = null;

        if (preferDatabase)
        {
            records = db.RawUserLogs
                .AsNoTracking()
                .OrderBy(row => row.Id)
                .Select(row => new ReconstructionLogRecord(
                    row.LogId,
                    row.OccurredAtUtc,
                    row.Title,
                    row.Category,
                    row.RawJson))
                .ToList();

            readerStats = new JsonlLogReader.ReadStats();
        }
        else
        {
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

            readerStats = read.Stats;
            records = read.Records.Select(record => new ReconstructionLogRecord(
                    LogId: record.LogId,
                    OccurredAtUtc: record.OccurredAtUtc,
                    Title: record.Title,
                    Category: record.Category,
                    RawJson: record.RawJson))
                .ToList();
        }

        var extract = LogEventExtractor.Extract(records);
        var events = new List<ReconstructionEvent>();

        foreach (var ev in extract.Events)
        {
            ct.ThrowIfCancellationRequested();
            events.Add(ev);
        }

        var reconstructed = HappyTimelineReconstructor.RunForward(events);

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

        db.DerivedGymTrains.RemoveRange(db.DerivedGymTrains);
        db.DerivedHappyEvents.RemoveRange(db.DerivedHappyEvents);
        db.SaveChanges();

        db.DerivedGymTrains.AddRange(reconstructed.DerivedGymTrains.Select(MapDerivedGymTrain));
        db.DerivedHappyEvents.AddRange(reconstructed.DerivedHappyEvents.Select((ev, index) => MapDerivedHappyEvent(ev, index)));
        db.SaveChanges();

        var stats = new ReconstructionStats(
            LinesRead: readerStats?.LinesRead ?? records.Count,
            MalformedLines: readerStats?.MalformedLines ?? 0,
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

    private static DerivedGymTrainEntity MapDerivedGymTrain(DerivedGymTrain row)
        => new()
        {
            LogId = row.LogId,
            OccurredAtUtc = row.OccurredAtUtc,
            HappyBeforeTrain = row.HappyBeforeTrain,
            HappyAfterTrain = row.HappyAfterTrain,
            HappyUsed = row.HappyUsed,
            RegenTicksApplied = row.RegenTicksApplied,
            RegenHappyGained = row.RegenHappyGained,
            MaxHappyAtTimeUtc = row.MaxHappyAtTimeUtc,
            ClampedToMax = row.ClampedToMax,
        };

    private static DerivedHappyEventEntity MapDerivedHappyEvent(DerivedHappyEvent row, int sortOrder)
        => new()
        {
            EventId = row.EventId,
            EventType = row.EventType,
            OccurredAtUtc = row.OccurredAtUtc,
            SourceLogId = row.SourceLogId,
            SortOrder = sortOrder,
            HappyBeforeEvent = row.HappyBeforeEvent,
            HappyAfterEvent = row.HappyAfterEvent,
            Delta = row.Delta,
            HappyUsed = row.HappyUsed,
            MaxHappyAtTimeUtc = row.MaxHappyAtTimeUtc,
            ClampedToMax = row.ClampedToMax,
            Note = null,
        };
}
