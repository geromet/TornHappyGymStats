using System.Collections.Concurrent;
using HappyGymStats.Core.Fetch;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Surfaces;
using HappyGymStats.Core.Torn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.Core.Import;

/// <summary>
/// Manages a single long-running import at a time.
/// The API key is accepted per-request and never persisted — it is only held
/// in memory for the duration of the active fetch, then discarded.
/// </summary>
public sealed class ImportOrchestrator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SurfacesCacheWriter _surfacesCacheWriter;

    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly ConcurrentQueue<ImportJobRequest> _queue = new();

    private volatile ImportJobStatus? _latest;

    private readonly ILogger<ImportOrchestrator> _logger;

    public ImportOrchestrator(IServiceScopeFactory scopeFactory, SurfacesCacheWriter surfacesCacheWriter, ILogger<ImportOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
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

        // Generate AnonymousId here for fresh imports so callers can use it immediately
        // (e.g. to create an IdentityMap entry before the background job runs).
        // Resume imports resolve the existing AnonymousId from the DB in RunImportAsync.
        var anonymousId = fresh ? Guid.NewGuid() : Guid.Empty;

        var status = new ImportJobStatus(
            Id: Guid.NewGuid().ToString("N"),
            AnonymousId: anonymousId,
            Outcome: "queued",
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null,
            PagesFetched: 0,
            LogsFetched: 0,
            LogsAppended: 0,
            ErrorMessage: null);

        _latest = status;
        _queue.Enqueue(new ImportJobRequest(apiKey, fresh, status.Id, anonymousId));
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
            using var scope = _scopeFactory.CreateScope();
            var tornClient = scope.ServiceProvider.GetRequiredService<TornApiClient>();
            var logFetcher = scope.ServiceProvider.GetRequiredService<LogFetcher>();
            var perkFetcher = scope.ServiceProvider.GetRequiredService<PerkLogFetcher>();
            var reconstructionRunner = scope.ServiceProvider.GetRequiredService<ReconstructionRunner>();
            var importRunRepo = scope.ServiceProvider.GetRequiredService<IImportRunRepository>();

            // Validate the API key and log the Torn player ID, but do not store it — TornPlayerId is PII
            // that will be encrypted into IdentityMap in Phase 2.
            var tornPlayerId = await tornClient.GetPlayerIdAsync(request.ApiKey, stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Import job {JobId} API key validated for Torn player {TornPlayerId}", request.JobId, tornPlayerId);

            var mode = request.Fresh ? FetchMode.Fresh : FetchMode.Resume;

            Guid anonymousId = request.AnonymousId != Guid.Empty
                ? request.AnonymousId
                : await importRunRepo.ResolveAnonymousIdAsync(stoppingToken).ConfigureAwait(false) ?? Guid.NewGuid();

            _logger.LogInformation("Import job {JobId} using AnonymousId {AnonymousId}", request.JobId, anonymousId);

            var options = FetchOptions.Default(
                new Uri("https://api.torn.com/v2/user/log?cat=25"),
                TimeSpan.FromMilliseconds(1100));

            int pagesRunning = 0;

            var result = await logFetcher.RunAsync(
                apiKey: request.ApiKey,
                anonymousId: anonymousId,
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

            var perkOptions = FetchOptions.Default(
                new Uri("https://api.torn.com/v2/user/log"),
                TimeSpan.FromMilliseconds(1100));

            var perkResult = await perkFetcher.RunAsync(
                apiKey: request.ApiKey,
                anonymousId: anonymousId,
                logTypes: PerkLogTypes.All,
                options: perkOptions,
                ct: stoppingToken,
                log: msg => _logger.LogInformation("Import job {JobId} [perks]: {Message}", request.JobId, msg)
            ).ConfigureAwait(false);

            _logger.LogInformation(
                "Import job {JobId} perk fetch complete: typesCompleted={Types} appended={Appended}",
                request.JobId,
                perkResult.LogTypesCompleted,
                perkResult.TotalLogsAppended);

            var reconstruction = await reconstructionRunner.RunAsync(
                anonymousId: anonymousId,
                currentHappy: 0,
                anchorTimeUtc: DateTimeOffset.UtcNow,
                ct: stoppingToken);

            if (!reconstruction.Success)
            {
                throw new InvalidOperationException(reconstruction.ErrorMessage ?? "Reconstruction failed after import.");
            }

            _logger.LogInformation(
                "Import job {JobId} reconstruction complete: gymTrains={GymTrains} warnings={Warnings}",
                request.JobId,
                reconstruction.DerivedGymTrains.Count,
                reconstruction.Stats?.WarningCount ?? 0);

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

    private sealed record ImportJobRequest(string ApiKey, bool Fresh, string JobId, Guid AnonymousId);
}

public sealed record ImportJobStatus(
    string Id,
    Guid AnonymousId,
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
