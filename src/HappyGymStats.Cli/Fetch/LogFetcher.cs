using System.Text.Json;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Storage;
using HappyGymStats.Storage.Models;
using HappyGymStats.Torn;
using HappyGymStats.Torn.Models;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Fetch;

public enum FetchMode
{
    Fresh,
    Resume,
}

public sealed record FetchRunResult(
    Checkpoint Checkpoint,
    int PagesFetched,
    long LogsFetched,
    long LogsAppended);

public sealed class LogFetcher
{
    private readonly AppPaths _paths;
    private readonly TornApiClient _client;

    public LogFetcher(AppPaths paths, TornApiClient client)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<FetchRunResult> RunAsync(
        string apiKey,
        FetchMode mode,
        FetchOptions options,
        CancellationToken ct,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must be provided.", nameof(apiKey));

        if (options is null)
            throw new ArgumentNullException(nameof(options));

        Directory.CreateDirectory(_paths.DataDirectory);
        Directory.CreateDirectory(_paths.QuarantineDirectory);

        var databasePath = SqlitePaths.ResolveDatabasePath(_paths.DataDirectory);
        var dbOptions = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var db = new HappyGymStatsDbContext(dbOptions);
        await db.Database.MigrateAsync(ct);

        var scan = JsonlLogStore.ScanAndQuarantine(_paths.LogsJsonlPath, _paths.QuarantineDirectory);
        if (scan.MalformedLines > 0)
        {
            log?.Invoke($"Local JSONL store scan found {scan.MalformedLines} malformed line(s) (see quarantine directory). Continuing with best-effort dedupe.");
        }

        var existingIds = scan.ExistingIds;
        foreach (var dbLogId in await db.RawUserLogs.AsNoTracking().Select(row => row.LogId).ToListAsync(ct))
            existingIds.Add(dbLogId);

        var checkpoint = CheckpointStore.TryRead(_paths.CheckpointPath)
                         ?? await ReadCheckpointFromDatabaseAsync(db, ct)
                         ?? new Checkpoint(
                             NextUrl: null,
                             LastLogId: scan.LastLogId,
                             LastLogTimestamp: scan.LastLogTimestamp,
                             LastLogTitle: scan.LastLogTitle,
                             LastLogCategory: scan.LastLogCategory,
                             TotalFetchedCount: 0,
                             TotalAppendedCount: existingIds.Count,
                             LastRunStartedAt: null,
                             LastRunCompletedAt: null,
                             LastRunOutcome: null,
                             LastErrorMessage: null,
                             LastErrorAt: null);

        if (checkpoint.TotalAppendedCount < existingIds.Count)
            checkpoint = checkpoint with { TotalAppendedCount = existingIds.Count };

        checkpoint = checkpoint with
        {
            LastRunStartedAt = DateTimeOffset.UtcNow,
            LastRunCompletedAt = null,
            LastRunOutcome = "running",
            LastErrorMessage = null,
            LastErrorAt = null,
        };

        var importRun = new ImportRunEntity
        {
            StartedAtUtc = checkpoint.LastRunStartedAt ?? DateTimeOffset.UtcNow,
            Outcome = "running",
            ErrorMessage = null,
            PagesFetched = 0,
            LogsFetched = 0,
            LogsAppended = 0,
        };
        db.ImportRuns.Add(importRun);
        await SaveCheckpointAsync(db, checkpoint, ct);
        await db.SaveChangesAsync(ct);

        Uri? nextUrl;
        if (mode == FetchMode.Fresh)
        {
            nextUrl = options.FreshStartUrl;
            checkpoint = checkpoint with { NextUrl = nextUrl.OriginalString };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(checkpoint.NextUrl))
            {
                checkpoint = checkpoint with
                {
                    LastRunCompletedAt = DateTimeOffset.UtcNow,
                    LastRunOutcome = "noop",
                };
                importRun.CompletedAtUtc = checkpoint.LastRunCompletedAt;
                importRun.Outcome = "noop";
                CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
                await SaveCheckpointAsync(db, checkpoint, ct);
                await db.SaveChangesAsync(ct);
                log?.Invoke("No resume cursor present in checkpoint. Nothing to resume.");
                return new FetchRunResult(checkpoint, PagesFetched: 0, LogsFetched: 0, LogsAppended: 0);
            }

            if (!Uri.TryCreate(checkpoint.NextUrl, UriKind.Absolute, out var parsed))
            {
                checkpoint = checkpoint with
                {
                    LastRunCompletedAt = DateTimeOffset.UtcNow,
                    LastRunOutcome = "failed",
                    LastErrorMessage = $"Checkpoint NextUrl is not a valid absolute URI: '{checkpoint.NextUrl}'.",
                    LastErrorAt = DateTimeOffset.UtcNow,
                };
                importRun.CompletedAtUtc = checkpoint.LastRunCompletedAt;
                importRun.Outcome = "failed";
                importRun.ErrorMessage = checkpoint.LastErrorMessage;
                CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
                await SaveCheckpointAsync(db, checkpoint, ct);
                await db.SaveChangesAsync(ct);
                throw new InvalidDataException(checkpoint.LastErrorMessage);
            }

            nextUrl = parsed;
        }

        var pagesFetched = 0;
        long logsFetched = 0;
        long logsAppended = 0;
        var throttleFirst = true;

        try
        {
            while (nextUrl is not null)
            {
                ct.ThrowIfCancellationRequested();

                if (!throttleFirst && options.ThrottleDelay > TimeSpan.Zero)
                {
                    log?.Invoke($"Throttling for {options.ThrottleDelay.TotalMilliseconds:0} ms...");
                    await Task.Delay(options.ThrottleDelay, ct).ConfigureAwait(false);
                }

                throttleFirst = false;

                var page = await FetchWithRetryAsync(apiKey, nextUrl, options, ct, log).ConfigureAwait(false);

                pagesFetched++;
                logsFetched += page.Logs.Count;

                var newLogs = new List<UserLog>(page.Logs.Count);
                UserLog? lastAppended = null;

                foreach (var logItem in page.Logs)
                {
                    if (string.IsNullOrEmpty(logItem.Id))
                        continue;

                    if (!existingIds.Add(logItem.Id))
                        continue;

                    newLogs.Add(logItem);
                    lastAppended = logItem;
                }

                if (newLogs.Count > 0)
                {
                    JsonlLogStore.Append(_paths.LogsJsonlPath, newLogs.Select(row => row.Raw));
                    db.RawUserLogs.AddRange(newLogs.Select(MapRawUserLog));
                    await db.SaveChangesAsync(ct);
                    logsAppended += newLogs.Count;

                    if (lastAppended is not null)
                    {
                        checkpoint = checkpoint with
                        {
                            LastLogId = lastAppended.Id,
                            LastLogTimestamp = SafeUnixSeconds(lastAppended.Timestamp),
                            LastLogTitle = lastAppended.Title,
                            LastLogCategory = lastAppended.Category,
                        };
                    }
                }

                checkpoint = checkpoint with
                {
                    NextUrl = page.NextUrl?.OriginalString,
                    TotalFetchedCount = checkpoint.TotalFetchedCount + page.Logs.Count,
                    TotalAppendedCount = checkpoint.TotalAppendedCount + newLogs.Count,
                };

                importRun.PagesFetched = pagesFetched;
                importRun.LogsFetched = checked((int)Math.Min(int.MaxValue, logsFetched));
                importRun.LogsAppended = checked((int)Math.Min(int.MaxValue, logsAppended));

                CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
                await SaveCheckpointAsync(db, checkpoint, ct);
                await db.SaveChangesAsync(ct);

                if (page.Logs.Count == 0)
                {
                    log?.Invoke("Torn API returned an empty page. Stopping.");
                    nextUrl = null;
                    break;
                }

                if (page.NextUrl is null)
                {
                    log?.Invoke("No next cursor provided by the server. Reached end of history.");
                    nextUrl = null;
                    break;
                }

                log?.Invoke($"Page {pagesFetched}: fetched={page.Logs.Count}, appended={newLogs.Count}, next={(page.NextUrl is null ? "(none)" : "present")}");
                nextUrl = page.NextUrl;
            }

            checkpoint = checkpoint with
            {
                LastRunCompletedAt = DateTimeOffset.UtcNow,
                LastRunOutcome = "completed",
            };

            importRun.CompletedAtUtc = checkpoint.LastRunCompletedAt;
            importRun.Outcome = "completed";
            importRun.PagesFetched = pagesFetched;
            importRun.LogsFetched = checked((int)Math.Min(int.MaxValue, logsFetched));
            importRun.LogsAppended = checked((int)Math.Min(int.MaxValue, logsAppended));

            CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
            await SaveCheckpointAsync(db, checkpoint, ct);
            await db.SaveChangesAsync(ct);
            return new FetchRunResult(checkpoint, pagesFetched, logsFetched, logsAppended);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            checkpoint = checkpoint with
            {
                LastRunCompletedAt = DateTimeOffset.UtcNow,
                LastRunOutcome = "cancelled",
            };

            importRun.CompletedAtUtc = checkpoint.LastRunCompletedAt;
            importRun.Outcome = "cancelled";
            importRun.PagesFetched = pagesFetched;
            importRun.LogsFetched = checked((int)Math.Min(int.MaxValue, logsFetched));
            importRun.LogsAppended = checked((int)Math.Min(int.MaxValue, logsAppended));

            CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
            await SaveCheckpointAsync(db, checkpoint, ct);
            await db.SaveChangesAsync(ct);
            throw;
        }
        catch (Exception ex)
        {
            checkpoint = checkpoint with
            {
                LastRunCompletedAt = DateTimeOffset.UtcNow,
                LastRunOutcome = "failed",
                LastErrorMessage = ex.Message,
                LastErrorAt = DateTimeOffset.UtcNow,
            };

            importRun.CompletedAtUtc = checkpoint.LastRunCompletedAt;
            importRun.Outcome = "failed";
            importRun.ErrorMessage = ex.Message;
            importRun.PagesFetched = pagesFetched;
            importRun.LogsFetched = checked((int)Math.Min(int.MaxValue, logsFetched));
            importRun.LogsAppended = checked((int)Math.Min(int.MaxValue, logsAppended));

            CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
            await SaveCheckpointAsync(db, checkpoint, ct);
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task<UserLogPage> FetchWithRetryAsync(
        string apiKey,
        Uri url,
        FetchOptions options,
        CancellationToken ct,
        Action<string>? log)
    {
        var attempt = 0;
        var backoff = options.InitialBackoffDelay;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await _client.GetUserLogPageAsync(apiKey, url, ct).ConfigureAwait(false);
            }
            catch (TornApiException ex) when (ex.IsRetryable)
            {
                attempt++;
                if (attempt > options.MaxRetryAttempts)
                {
                    log?.Invoke($"Transient error retry budget exhausted after {options.MaxRetryAttempts} attempt(s). Last error: {ex.Message}");
                    throw;
                }

                var delay = backoff;
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, options.MaxBackoffDelay.TotalMilliseconds));

                log?.Invoke($"Transient error (attempt {attempt}/{options.MaxRetryAttempts}) - backing off {delay.TotalSeconds:0.0}s: {ex.Message}");
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task<Checkpoint?> ReadCheckpointFromDatabaseAsync(HappyGymStatsDbContext db, CancellationToken ct)
    {
        var entity = await db.ImportCheckpoints.AsNoTracking().SingleOrDefaultAsync(row => row.Name == "default", ct);
        if (entity is null)
            return null;

        return new Checkpoint(
            entity.NextUrl,
            entity.LastLogId,
            entity.LastLogTimestamp,
            entity.LastLogTitle,
            entity.LastLogCategory,
            entity.TotalFetchedCount,
            entity.TotalAppendedCount,
            entity.LastRunStartedAt,
            entity.LastRunCompletedAt,
            entity.LastRunOutcome,
            entity.LastErrorMessage,
            entity.LastErrorAt);
    }

    private static async Task SaveCheckpointAsync(HappyGymStatsDbContext db, Checkpoint checkpoint, CancellationToken ct)
    {
        var entity = await db.ImportCheckpoints.SingleOrDefaultAsync(row => row.Name == "default", ct);
        if (entity is null)
        {
            entity = new ImportCheckpointEntity { Name = "default" };
            db.ImportCheckpoints.Add(entity);
        }

        entity.NextUrl = checkpoint.NextUrl;
        entity.LastLogId = checkpoint.LastLogId;
        entity.LastLogTimestamp = checkpoint.LastLogTimestamp;
        entity.LastLogTitle = checkpoint.LastLogTitle;
        entity.LastLogCategory = checkpoint.LastLogCategory;
        entity.TotalFetchedCount = checkpoint.TotalFetchedCount;
        entity.TotalAppendedCount = checkpoint.TotalAppendedCount;
        entity.LastRunStartedAt = checkpoint.LastRunStartedAt;
        entity.LastRunCompletedAt = checkpoint.LastRunCompletedAt;
        entity.LastRunOutcome = checkpoint.LastRunOutcome;
        entity.LastErrorMessage = checkpoint.LastErrorMessage;
        entity.LastErrorAt = checkpoint.LastErrorAt;
    }

    private static RawUserLogEntity MapRawUserLog(UserLog log)
        => new()
        {
            LogId = log.Id,
            OccurredAtUtc = SafeUnixSeconds(log.Timestamp) ?? DateTimeOffset.UnixEpoch,
            Title = log.Title,
            Category = log.Category,
            RawJson = JsonSerializer.Serialize(log.Raw),
        };

    private static DateTimeOffset? SafeUnixSeconds(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch
        {
            return null;
        }
    }
}
