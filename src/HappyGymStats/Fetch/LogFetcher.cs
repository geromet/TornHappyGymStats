using System.Text.Json;

using HappyGymStats.Storage;
using HappyGymStats.Storage.Models;
using HappyGymStats.Torn;
using HappyGymStats.Torn.Models;

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

        var scan = JsonlLogStore.ScanAndQuarantine(_paths.LogsJsonlPath, _paths.QuarantineDirectory);
        if (scan.MalformedLines > 0)
        {
            log?.Invoke($"Local JSONL store scan found {scan.MalformedLines} malformed line(s) (see quarantine directory). Continuing with best-effort dedupe.");
        }

        var existingIds = scan.ExistingIds;

        var checkpoint = CheckpointStore.TryRead(_paths.CheckpointPath)
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

        // If old checkpoint had counts lower than the known on-disk store, correct it upward.
        if (checkpoint.TotalAppendedCount < existingIds.Count)
        {
            checkpoint = checkpoint with { TotalAppendedCount = existingIds.Count };
        }

        checkpoint = checkpoint with
        {
            LastRunStartedAt = DateTimeOffset.UtcNow,
            LastRunCompletedAt = null,
            LastRunOutcome = "running",
            LastErrorMessage = null,
            LastErrorAt = null,
        };

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
                CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
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
                CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
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

                var newRaw = new List<JsonElement>(page.Logs.Count);
                UserLog? lastAppended = null;

                foreach (var logItem in page.Logs)
                {
                    if (string.IsNullOrEmpty(logItem.Id))
                        continue;

                    if (!existingIds.Add(logItem.Id))
                        continue;

                    newRaw.Add(logItem.Raw);
                    lastAppended = logItem;
                }

                if (newRaw.Count > 0)
                {
                    JsonlLogStore.Append(_paths.LogsJsonlPath, newRaw);
                    logsAppended += newRaw.Count;

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
                    TotalAppendedCount = checkpoint.TotalAppendedCount + newRaw.Count,
                };

                // Durable progress marker: always write after handling a page.
                CheckpointStore.Write(_paths.CheckpointPath, checkpoint);

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

                log?.Invoke($"Page {pagesFetched}: fetched={page.Logs.Count}, appended={newRaw.Count}, next={(page.NextUrl is null ? "(none)" : "present")}");

                nextUrl = page.NextUrl;
            }

            checkpoint = checkpoint with
            {
                LastRunCompletedAt = DateTimeOffset.UtcNow,
                LastRunOutcome = "completed",
            };

            CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
            return new FetchRunResult(checkpoint, pagesFetched, logsFetched, logsAppended);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            checkpoint = checkpoint with
            {
                LastRunCompletedAt = DateTimeOffset.UtcNow,
                LastRunOutcome = "cancelled",
            };

            CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
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

            CheckpointStore.Write(_paths.CheckpointPath, checkpoint);
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
