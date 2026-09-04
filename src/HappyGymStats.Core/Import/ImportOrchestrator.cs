using System.Collections.Concurrent;
using System.Text;
using HappyGymStats.Core.Fetch;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Surfaces;
using HappyGymStats.Core.Torn;
using HappyGymStats.Encryption;
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
    private readonly ILogger<ImportOrchestrator> _logger;

    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly ConcurrentQueue<ImportJobRequest> _queue = new();
    private readonly object _stateGate = new();
    private readonly Dictionary<Guid, ImportJobStatus> _latestByOwner = new();

    private ImportJobStatus? _active;

    public ImportOrchestrator(IServiceScopeFactory scopeFactory, SurfacesCacheWriter surfacesCacheWriter, ILogger<ImportOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _surfacesCacheWriter = surfacesCacheWriter;
        _logger = logger;
    }

    /// <summary>
    /// Returns only status owned by the supplied caller identity. There is deliberately
    /// no process-global latest status because import state is private tenant data.
    /// </summary>
    public ImportJobStatus? GetLatestForAnonymousId(Guid anonymousId)
    {
        if (anonymousId == Guid.Empty)
            return null;

        lock (_stateGate)
            return _latestByOwner.GetValueOrDefault(anonymousId);
    }

    /// <summary>
    /// Starts a new anonymous/fresh import with a newly allocated owner identity.
    /// A busy worker returns a tenant-neutral rejection and never another job's status.
    /// </summary>
    public ImportEnqueueResult TryEnqueueFresh(string apiKey, byte[]? publicKey = null)
        => TryEnqueueInternal(apiKey, fresh: true, Guid.NewGuid(), publicKey);

    /// <summary>
    /// Starts or resumes work for a server-authorized owner identity.
    /// </summary>
    public ImportEnqueueResult TryEnqueueForAnonymousId(
        string apiKey,
        Guid anonymousId,
        bool fresh,
        byte[]? publicKey = null)
    {
        if (anonymousId == Guid.Empty)
            throw new ArgumentException("AnonymousId must identify the import owner.", nameof(anonymousId));

        return TryEnqueueInternal(apiKey, fresh, anonymousId, publicKey);
    }

    private ImportEnqueueResult TryEnqueueInternal(string apiKey, bool fresh, Guid anonymousId, byte[]? publicKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        lock (_stateGate)
        {
            if (_active is { IsTerminal: false })
                return ImportEnqueueResult.Busy;

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

            _active = status;
            _latestByOwner[anonymousId] = status;
            _queue.Enqueue(new ImportJobRequest(apiKey, fresh, status.Id, anonymousId, publicKey));
            return ImportEnqueueResult.AcceptedJob(status);
        }
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
        Update(request.JobId, request.AnonymousId, s => s with { Outcome = "running" });

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tornClient = scope.ServiceProvider.GetRequiredService<TornApiClient>();
            var logFetcher = scope.ServiceProvider.GetRequiredService<LogFetcher>();
            var perkFetcher = scope.ServiceProvider.GetRequiredService<PerkLogFetcher>();
            var reconstructionRunner = scope.ServiceProvider.GetRequiredService<ReconstructionRunner>();
            var identityMapRepo = scope.ServiceProvider.GetRequiredService<IIdentityMapRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var tornPlayerId = await tornClient.GetPlayerIdAsync(request.ApiKey, stoppingToken).ConfigureAwait(false);
            var mode = request.Fresh ? FetchMode.Fresh : FetchMode.Resume;
            var anonymousId = request.AnonymousId;

            _logger.LogInformation("Import job {JobId} API key validated for AnonymousId {AnonymousId}", request.JobId, anonymousId);

            if (request.PublicKey is not null)
            {
                var plaintext = Encoding.UTF8.GetBytes(tornPlayerId.ToString());
                var encrypted = Ecies.Encrypt(request.PublicKey, plaintext);
                await identityMapRepo.StoreEncryptedTornPlayerIdAsync(anonymousId, encrypted, stoppingToken).ConfigureAwait(false);
                await unitOfWork.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Import job {JobId} encrypted TornPlayerId stored.", request.JobId);
            }

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

                    Update(request.JobId, request.AnonymousId, s => s with
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
                publicKey: request.PublicKey,
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
                throw new InvalidOperationException(reconstruction.ErrorMessage ?? "Reconstruction failed after import.");

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

            Update(request.JobId, request.AnonymousId, s => s with
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
            Update(request.JobId, request.AnonymousId, s => s with
            {
                Outcome = "cancelled",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job {JobId} failed", request.JobId);
            Update(request.JobId, request.AnonymousId, s => s with
            {
                Outcome = "failed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message,
            });
        }
    }

    private void Update(string jobId, Guid anonymousId, Func<ImportJobStatus, ImportJobStatus> mutate)
    {
        lock (_stateGate)
        {
            if (!_latestByOwner.TryGetValue(anonymousId, out var current) || current.Id != jobId)
                return;

            var updated = mutate(current);
            _latestByOwner[anonymousId] = updated;

            if (_active?.Id == jobId)
                _active = updated.IsTerminal ? null : updated;
        }
    }

    private sealed record ImportJobRequest(string ApiKey, bool Fresh, string JobId, Guid AnonymousId, byte[]? PublicKey);
}

public sealed record ImportEnqueueResult(bool Accepted, ImportJobStatus? Status)
{
    public static ImportEnqueueResult Busy { get; } = new(false, null);

    public static ImportEnqueueResult AcceptedJob(ImportJobStatus status) => new(true, status);
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
