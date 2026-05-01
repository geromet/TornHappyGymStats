using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Export;
using HappyGymStats.Reconstruction;
using HappyGymStats.Storage.Models;
using Microsoft.EntityFrameworkCore;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Storage;

public static class LegacySqliteMigrator
{
    public sealed record RunResult(
        bool Success,
        string? ErrorMessage,
        string DatabasePath,
        int RawLogsImported,
        int DerivedGymTrainsImported,
        int DerivedHappyEventsImported,
        bool CheckpointImported);

    public static async Task<RunResult> RunAsync(
        AppPaths paths,
        string databasePath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");

            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new HappyGymStatsDbContext(options);
            await db.Database.MigrateAsync(ct);

            var rawRead = JsonlLogReader.Read(paths.LogsJsonlPath);
            if (!rawRead.Success)
            {
                return new RunResult(false, rawRead.ErrorMessage, databasePath, 0, 0, 0, false);
            }

            var derivedTrainRead = DerivedGymTrainReader.Read(paths.DerivedGymTrainsJsonlPath);
            if (derivedTrainRead.ErrorMessage is not null)
                return new RunResult(false, derivedTrainRead.ErrorMessage, databasePath, 0, 0, 0, false);

            var derivedHappyRead = DerivedHappyEventReader.Read(paths.DerivedHappyEventsJsonlPath);
            if (derivedHappyRead.ErrorMessage is not null)
                return new RunResult(false, derivedHappyRead.ErrorMessage, databasePath, 0, 0, 0, false);

            var checkpoint = CheckpointStore.TryRead(paths.CheckpointPath);

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            db.RawUserLogs.RemoveRange(db.RawUserLogs);
            db.DerivedGymTrains.RemoveRange(db.DerivedGymTrains);
            db.DerivedHappyEvents.RemoveRange(db.DerivedHappyEvents);
            db.ImportCheckpoints.RemoveRange(db.ImportCheckpoints);
            await db.SaveChangesAsync(ct);

            var rawRows = rawRead.Records
                .Select(record => new RawUserLogEntity
                {
                    LogId = record.LogId,
                    OccurredAtUtc = record.OccurredAtUtc,
                    Title = record.Title,
                    Category = record.Category,
                    RawJson = record.RawJson,
                })
                .ToList();

            db.RawUserLogs.AddRange(rawRows);

            var derivedTrainRows = derivedTrainRead.Records.Values
                .Select(record => new DerivedGymTrainEntity
                {
                    LogId = record.LogId,
                    OccurredAtUtc = record.OccurredAtUtc,
                    HappyBeforeTrain = record.HappyBeforeTrain,
                    HappyAfterTrain = record.HappyAfterTrain,
                    HappyUsed = record.HappyUsed,
                    RegenTicksApplied = record.RegenTicksApplied,
                    RegenHappyGained = record.RegenHappyGained,
                    MaxHappyAtTimeUtc = record.MaxHappyAtTimeUtc,
                    ClampedToMax = record.ClampedToMax,
                })
                .ToList();

            db.DerivedGymTrains.AddRange(derivedTrainRows);

            var derivedHappyRows = derivedHappyRead.AllEvents
                .Select((record, index) => new DerivedHappyEventEntity
                {
                    EventId = record.EventId,
                    EventType = record.EventType,
                    OccurredAtUtc = record.OccurredAtUtc,
                    SourceLogId = record.SourceLogId,
                    SortOrder = index,
                    HappyBeforeEvent = record.HappyBeforeEvent,
                    HappyAfterEvent = record.HappyAfterEvent,
                    Delta = record.Delta,
                    HappyUsed = record.HappyUsed,
                    MaxHappyAtTimeUtc = record.MaxHappyAtTimeUtc,
                    ClampedToMax = record.ClampedToMax,
                    Note = null,
                })
                .ToList();

            db.DerivedHappyEvents.AddRange(derivedHappyRows);

            if (checkpoint is not null)
            {
                db.ImportCheckpoints.Add(MapCheckpoint(checkpoint));
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new RunResult(
                Success: true,
                ErrorMessage: null,
                DatabasePath: databasePath,
                RawLogsImported: rawRows.Count,
                DerivedGymTrainsImported: derivedTrainRows.Count,
                DerivedHappyEventsImported: derivedHappyRows.Count,
                CheckpointImported: checkpoint is not null);
        }
        catch (Exception ex)
        {
            return new RunResult(false, ex.Message, databasePath, 0, 0, 0, false);
        }
    }

    private static ImportCheckpointEntity MapCheckpoint(Checkpoint checkpoint)
        => new()
        {
            Name = "default",
            NextUrl = checkpoint.NextUrl,
            LastLogId = checkpoint.LastLogId,
            LastLogTimestamp = checkpoint.LastLogTimestamp,
            LastLogTitle = checkpoint.LastLogTitle,
            LastLogCategory = checkpoint.LastLogCategory,
            TotalFetchedCount = checkpoint.TotalFetchedCount,
            TotalAppendedCount = checkpoint.TotalAppendedCount,
            LastRunStartedAt = checkpoint.LastRunStartedAt,
            LastRunCompletedAt = checkpoint.LastRunCompletedAt,
            LastRunOutcome = checkpoint.LastRunOutcome,
            LastErrorMessage = checkpoint.LastErrorMessage,
            LastErrorAt = checkpoint.LastErrorAt,
        };
}
