using System.Collections.Concurrent;
using HappyGymStats.Fetch;
using HappyGymStats.Storage;
using HappyGymStats.Torn;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.Api;

/// <summary>
/// Manages a single long-running import at a time.
/// The API key is accepted per-request and never persisted — it is only held
/// in memory for the duration of the active fetch, then discarded.
/// </summary>
public sealed class ImportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _databasePath;
    private readonly SurfacesCacheWriter _surfacesCacheWriter;

    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly ConcurrentQueue<ImportJobRequest> _queue = new();

    private volatile ImportJobStatus? _latest;

    private readonly ILogger<ImportService> _logger;

    public ImportService(IServiceScopeFactory scopeFactory, string databasePath, SurfacesCacheWriter surfacesCacheWriter, ILogger<ImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _databasePath = databasePath;
        _surfacesCacheWriter = surfacesCacheWriter;
        _logger = logger;
    }

    public ImportJobStatus? Latest => _latest;

    /// <summary>
    /// Enqueue an import if none is already running.
    /// Returns the status of the enqueued (or already-running) job.
    /// </summary>
    public ImportJobStatus Enqueue(string apiKey, bool fresh)
    {
        if (_latest is { IsTerminal: false })
            return _latest;

        var status = new ImportJobStatus(
            Id: Guid.NewGuid().ToString("N"),
            Outcome: "queued",
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null,
            PagesFetched: 0,
            LogsFetched: 0,
            LogsAppended: 0,
            ErrorMessage: null);

        _latest = status;
        _queue.Enqueue(new ImportJobRequest(apiKey, fresh, status.Id));
        return status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var request))
            {
                await Task.Delay(200, stoppingToken).ConfigureAwait(false);
                continue;
            }

            await _slot.WaitAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await RunImportAsync(request, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _slot.Release();
            }
        }
    }

    private async Task RunImportAsync(ImportJobRequest request, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Import job {JobId} started. Mode={Mode}", request.JobId, request.Fresh ? "fresh" : "resume");
        Update(request.JobId, s => s with { Outcome = "running" });

        try
        {
            var paths = AppPaths.Default();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var tornClient = new TornApiClient(httpClient);
            var fetcher = new LogFetcher(paths, tornClient);

            var mode = request.Fresh ? FetchMode.Fresh : FetchMode.Resume;
            var options = FetchOptions.Default(
                new Uri("https://api.torn.com/v2/user/log?cat=25"),
                TimeSpan.FromMilliseconds(1100));

            int pagesRunning = 0;

            var result = await fetcher.RunAsync(
                apiKey: request.ApiKey,
                mode: mode,
                options: options,
                ct: stoppingToken,
                log: msg =>
                {
                    _logger.LogInformation("Import job {JobId}: {Message}", request.JobId, msg);
                    if (msg.StartsWith("Page "))
                        pagesRunning++;

                    Update(request.JobId, s => s with
                    {
                        PagesFetched = pagesRunning,
                    });
                }).ConfigureAwait(false);

            var syncedAtUtc = DateTimeOffset.UtcNow;
            var version = $"{syncedAtUtc:O}-{request.JobId}";
            await _surfacesCacheWriter.WriteLatestAsync(version, syncedAtUtc, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Import job {JobId} completed: pages={Pages} fetched={Fetched} appended={Appended}",
                request.JobId,
                result.PagesFetched,
                result.LogsFetched,
                result.LogsAppended);

            Update(request.JobId, s => s with
            {
                Outcome = "completed",
                CompletedAtUtc = syncedAtUtc,
                PagesFetched = result.PagesFetched,
                LogsFetched = result.LogsFetched,
                LogsAppended = result.LogsAppended,
            });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Update(request.JobId, s => s with
            {
                Outcome = "cancelled",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job {JobId} failed", request.JobId);
            Update(request.JobId, s => s with
            {
                Outcome = "failed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message,
            });
        }
    }

    private void Update(string jobId, Func<ImportJobStatus, ImportJobStatus> mutate)
    {
        if (_latest?.Id == jobId)
            _latest = mutate(_latest);
    }

    private sealed record ImportJobRequest(string ApiKey, bool Fresh, string JobId);
}

public sealed record ImportJobStatus(
    string Id,
    string Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PagesFetched,
    long LogsFetched,
    long LogsAppended,
    string? ErrorMessage)
{
    public bool IsTerminal => Outcome is "completed" or "failed" or "cancelled";
}
