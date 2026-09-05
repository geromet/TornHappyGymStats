using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.WarPoller;

public interface IWarPollerNotifier
{
    Task NotifyWarStateUpdatedAsync(CancellationToken cancellationToken);
}

public sealed class WarPollerNotifier(
    HttpClient httpClient,
    WarPollerOptions options,
    ILogger<WarPollerNotifier> logger) : IWarPollerNotifier
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly WarPollerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<WarPollerNotifier> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task NotifyWarStateUpdatedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.HubNotifyUrl))
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.HubNotifyTimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.HubNotifyUrl);
        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);

        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            throw new HttpRequestException(
                $"War hub notify endpoint returned unexpected status code {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        _logger.LogDebug(
            "War poller hub notify succeeded for endpoint {HubNotifyUrl}.",
            DescribeEndpoint(_options.HubNotifyUrl));
    }

    private static string DescribeEndpoint(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "invalid";
        }

        return uri.GetLeftPart(UriPartial.Path);
    }
}

public sealed class WarPollerService
{
    private static readonly Regex AbsoluteUrlRegex = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApiKeyRegex = new(@"([?&]key=)[^&\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TornApiClient _tornApiClient;
    private readonly IWarStateRepository _warStateRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly WarPollerOptions _options;
    private readonly IWarPollerNotifier _warPollerNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WarPollerService> _logger;

    public WarPollerService(
        TornApiClient tornApiClient,
        IWarStateRepository warStateRepository,
        IUnitOfWork unitOfWork,
        WarPollerOptions options,
        IWarPollerNotifier warPollerNotifier,
        TimeProvider timeProvider,
        ILogger<WarPollerService> logger)
    {
        _tornApiClient = tornApiClient ?? throw new ArgumentNullException(nameof(tornApiClient));
        _warStateRepository = warStateRepository ?? throw new ArgumentNullException(nameof(warStateRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _warPollerNotifier = warPollerNotifier ?? throw new ArgumentNullException(nameof(warPollerNotifier));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _options.Validate();
    }

    public async Task<WarPollerTickResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var heartbeat = await _warStateRepository.GetHeartbeatAsync(_options.ScopeKey, CancellationToken.None);
        var activeWarId = heartbeat?.ActiveWarId;
        var now = _timeProvider.GetUtcNow();

        await PersistHeartbeatAsync(BuildHeartbeat(
            phase: "queued",
            updatedAtUtc: now,
            pollStartedAtUtc: null,
            pollCompletedAtUtc: null,
            retryCount: heartbeat?.RetryCount ?? 0,
            lastError: null,
            activeWarId: heartbeat?.ActiveWarId,
            staleAfterUtc: now.AddSeconds(_options.PollIntervalSeconds),
            failureBackoffSeconds: _options.FailureBackoffSeconds));

        var pollStartedAtUtc = _timeProvider.GetUtcNow();
        await PersistHeartbeatAsync(BuildHeartbeat(
            phase: "running",
            updatedAtUtc: pollStartedAtUtc,
            pollStartedAtUtc: pollStartedAtUtc,
            pollCompletedAtUtc: null,
            retryCount: heartbeat?.RetryCount ?? 0,
            lastError: null,
            activeWarId: heartbeat?.ActiveWarId,
            staleAfterUtc: pollStartedAtUtc.AddSeconds(_options.PollIntervalSeconds),
            failureBackoffSeconds: _options.FailureBackoffSeconds));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolution = await ResolveActiveWarAsync(cancellationToken);
            activeWarId = resolution?.WarId;
            return resolution is null
                ? await CompleteNoActiveWarAsync(pollStartedAtUtc, cancellationToken)
                : await CompleteActiveWarAsync(resolution, pollStartedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CompleteCancellationAsync(
                pollStartedAtUtc,
                heartbeat?.RetryCount ?? 0,
                activeWarId);
        }
        catch (TornApiException ex) when (ex.IsRetryable)
        {
            return await CompleteRetryableFailureAsync(
                ex,
                pollStartedAtUtc,
                heartbeat?.RetryCount ?? 0,
                activeWarId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await RecordFatalFailureAsync(
                ex,
                pollStartedAtUtc,
                heartbeat?.RetryCount ?? 0,
                activeWarId);
            throw;
        }
    }

    private async Task<WarPollerTickResult> CompleteNoActiveWarAsync(
        DateTimeOffset pollStartedAtUtc,
        CancellationToken cancellationToken)
    {
        var observedAtUtc = _timeProvider.GetUtcNow();
        await _warStateRepository.UpsertCurrentAsync(
            new WarCurrentEntity
            {
                ScopeKey = _options.ScopeKey,
                WarId = null,
                FactionId = _options.FactionId,
                FactionName = null,
                OpponentFactionId = null,
                OpponentFactionName = null,
                StartedAtUtc = null,
                EndsAtUtc = null,
                IsLive = false,
                ObservedAtUtc = observedAtUtc
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var completedAtUtc = _timeProvider.GetUtcNow();
        await PersistHeartbeatAsync(BuildHeartbeat(
            phase: "succeeded",
            updatedAtUtc: completedAtUtc,
            pollStartedAtUtc: pollStartedAtUtc,
            pollCompletedAtUtc: completedAtUtc,
            retryCount: 0,
            lastError: null,
            activeWarId: null,
            staleAfterUtc: completedAtUtc.AddSeconds(_options.PollIntervalSeconds),
            failureBackoffSeconds: _options.FailureBackoffSeconds));

        await TryNotifyHubAsync(cancellationToken);

        _logger.LogInformation("War poller found no active war for scope {ScopeKey} faction {FactionId}.", _options.ScopeKey, _options.FactionId);
        return new WarPollerTickResult("succeeded", null, TimeSpan.FromSeconds(_options.PollIntervalSeconds), false);
    }

    private async Task<WarPollerTickResult> CompleteActiveWarAsync(
        ActiveWarResolution resolution,
        DateTimeOffset pollStartedAtUtc,
        CancellationToken cancellationToken)
    {
        var report = await _tornApiClient.GetRankedWarReportAsync(_options.ApiKey, resolution.WarId, cancellationToken);
        var ourChainLapsesAtUtc = await TryGetOurChainDeadlineAsync(cancellationToken);
        var capturedAtUtc = _timeProvider.GetUtcNow();
        var persistedState = BuildPersistedState(resolution, report, capturedAtUtc, ourChainLapsesAtUtc);

        await _warStateRepository.UpsertCurrentAsync(persistedState.Current, cancellationToken);
        await _warStateRepository.ReplaceRosterSnapshotAsync(persistedState.Current.WarId!.Value, persistedState.RosterRows, cancellationToken);
        await _warStateRepository.AddScoreSampleAsync(persistedState.ScoreSample, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var completedAtUtc = _timeProvider.GetUtcNow();
        await PersistHeartbeatAsync(BuildHeartbeat(
            phase: "succeeded",
            updatedAtUtc: completedAtUtc,
            pollStartedAtUtc: pollStartedAtUtc,
            pollCompletedAtUtc: completedAtUtc,
            retryCount: 0,
            lastError: null,
            activeWarId: resolution.WarId,
            staleAfterUtc: completedAtUtc.AddSeconds(_options.PollIntervalSeconds),
            failureBackoffSeconds: _options.FailureBackoffSeconds));

        await TryNotifyHubAsync(cancellationToken);

        _logger.LogInformation("War poller persisted active war {WarId} for scope {ScopeKey}.", resolution.WarId, _options.ScopeKey);
        return new WarPollerTickResult("succeeded", resolution.WarId, TimeSpan.FromSeconds(_options.PollIntervalSeconds), true);
    }

    private async Task<WarPollerTickResult> CompleteCancellationAsync(
        DateTimeOffset pollStartedAtUtc,
        int retryCount,
        long? activeWarId)
    {
        var cancelledAtUtc = _timeProvider.GetUtcNow();
        await PersistHeartbeatAsync(BuildHeartbeat(
            phase: "cancelled",
            updatedAtUtc: cancelledAtUtc,
            pollStartedAtUtc: pollStartedAtUtc,
            pollCompletedAtUtc: cancelledAtUtc,
            retryCount: retryCount,
            lastError: "poll cancelled",
            activeWarId: activeWarId,
            staleAfterUtc: cancelledAtUtc.AddSeconds(_options.PollIntervalSeconds),
            failureBackoffSeconds: _options.FailureBackoffSeconds));

        _logger.LogInformation("War poller cancelled for scope {ScopeKey}.", _options.ScopeKey);
        return new WarPollerTickResult("cancelled", activeWarId, TimeSpan.Zero, false);
    }

    private async Task<WarPollerTickResult> CompleteRetryableFailureAsync(
        TornApiException exception,
        DateTimeOffset pollStartedAtUtc,
        int previousRetryCount,
        long? activeWarId,
        CancellationToken cancellationToken)
    {
        var retryCount = previousRetryCount + 1;
        var backoff = ComputeFailureBackoff(retryCount);
        var failedAtUtc = _timeProvider.GetUtcNow();
        var sanitizedMessage = BuildSanitizedErrorMessage(exception);

        await PersistHeartbeatAsync(BuildHeartbeat(
            phase: "retryable-failure",
            updatedAtUtc: failedAtUtc,
            pollStartedAtUtc: pollStartedAtUtc,
            pollCompletedAtUtc: failedAtUtc,
            retryCount: retryCount,
            lastError: sanitizedMessage,
            activeWarId: activeWarId,
            staleAfterUtc: failedAtUtc.Add(backoff),
            failureBackoffSeconds: (int)backoff.TotalSeconds));

        _logger.LogWarning(
            "War poller hit retryable failure for scope {ScopeKey}; backing off {BackoffSeconds}s. Error={Error}",
            _options.ScopeKey,
            (int)backoff.TotalSeconds,
            sanitizedMessage);

        try
        {
            await Task.Delay(backoff, _timeProvider, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelledAtUtc = _timeProvider.GetUtcNow();
            await PersistHeartbeatAsync(BuildHeartbeat(
                phase: "cancelled",
                updatedAtUtc: cancelledAtUtc,
                pollStartedAtUtc: pollStartedAtUtc,
                pollCompletedAtUtc: cancelledAtUtc,
                retryCount: retryCount,
                lastError: "poll cancelled",
                activeWarId: activeWarId,
                staleAfterUtc: cancelledAtUtc.AddSeconds(_options.PollIntervalSeconds),
                failureBackoffSeconds: _options.FailureBackoffSeconds));

            return new WarPollerTickResult("cancelled", activeWarId, TimeSpan.Zero, false);
        }

        return new WarPollerTickResult("retryable-failure", activeWarId, backoff, false);
    }

    private async Task RecordFatalFailureAsync(
        Exception exception,
        DateTimeOffset pollStartedAtUtc,
        int retryCount,
        long? activeWarId)
    {
        var failedAtUtc = _timeProvider.GetUtcNow();
        var sanitizedMessage = BuildSanitizedErrorMessage(exception);

        await PersistHeartbeatAsync(BuildHeartbeat(
            phase: "failed",
            updatedAtUtc: failedAtUtc,
            pollStartedAtUtc: pollStartedAtUtc,
            pollCompletedAtUtc: failedAtUtc,
            retryCount: retryCount,
            lastError: sanitizedMessage,
            activeWarId: activeWarId,
            staleAfterUtc: failedAtUtc.AddSeconds(_options.PollIntervalSeconds),
            failureBackoffSeconds: _options.FailureBackoffSeconds));
        _logger.LogError(
            "War poller failed for scope {ScopeKey}. ExceptionType={ExceptionType} Error={Error}",
            _options.ScopeKey,
            exception.GetType().Name,
            sanitizedMessage);
    }

    private async Task<ActiveWarResolution?> ResolveActiveWarAsync(CancellationToken cancellationToken)
    {
        var liveWarsTask = _tornApiClient.GetLiveFactionWarsAsync(_options.ApiKey, cancellationToken);
        var globalWarsTask = _tornApiClient.GetGlobalRankedWarsAsync(_options.ApiKey, cancellationToken);
        await Task.WhenAll(liveWarsTask, globalWarsTask);

        var liveWars = await liveWarsTask;
        var globalWars = await globalWarsTask;

        var globalCandidates = globalWars.Wars
            .Where(war => (war.FactionId == _options.FactionId || war.OpponentId == _options.FactionId)
                && war.End is null
                && war.WinnerFactionId is null)
            .ToList();

        if (globalCandidates.Count == 0)
        {
            return null;
        }

        if (globalCandidates.Count > 1)
        {
            throw new InvalidDataException($"Expected at most one active global war for faction {_options.FactionId}, but found {globalCandidates.Count}.");
        }

        var globalWar = globalCandidates[0];
        var liveCandidates = liveWars.Wars
            .Where(war => war.WarId == globalWar.WarId && war.IsLive)
            .ToList();

        if (liveCandidates.Count != 1)
        {
            throw new InvalidDataException($"Expected one live faction war for war {globalWar.WarId}, but found {liveCandidates.Count}.");
        }

        return new ActiveWarResolution(globalWar.WarId, liveCandidates[0]);
    }

    private PersistedWarState BuildPersistedState(
        ActiveWarResolution resolution,
        RankedWarReportResponse report,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset? ourChainLapsesAtUtc)
    {
        if (report.War.WarId != resolution.WarId)
        {
            throw new InvalidDataException($"Ranked war report id {report.War.WarId} did not match active war {resolution.WarId}.");
        }

        if (report.Factions.Count != 2)
        {
            throw new InvalidDataException($"Ranked war report for war {resolution.WarId} must contain exactly two factions, but found {report.Factions.Count}.");
        }

        var ourFaction = report.Factions.SingleOrDefault(faction => faction.FactionId == _options.FactionId)
            ?? throw new InvalidDataException($"Ranked war report for war {resolution.WarId} did not contain faction {_options.FactionId}.");

        var opponentFaction = report.Factions.SingleOrDefault(faction => faction.FactionId != _options.FactionId)
            ?? throw new InvalidDataException($"Ranked war report for war {resolution.WarId} did not contain an opponent faction.");

        var current = new WarCurrentEntity
        {
            ScopeKey = _options.ScopeKey,
            WarId = resolution.WarId,
            FactionId = ourFaction.FactionId,
            FactionName = ourFaction.Name,
            OpponentFactionId = opponentFaction.FactionId,
            OpponentFactionName = opponentFaction.Name,
            StartedAtUtc = report.War.Start,
            EndsAtUtc = report.War.End,
            IsLive = report.War.IsLive,
            ObservedAtUtc = capturedAtUtc
        };

        var rosterRows = report.Factions
            .SelectMany(faction => faction.Members.Select(member => new WarRosterSnapshotEntity
            {
                WarId = resolution.WarId,
                FactionId = faction.FactionId,
                FactionName = faction.Name,
                MemberId = member.UserId,
                MemberName = member.Name,
                Score = member.Score,
                Chain = member.Chain,
                Attacks = member.Attacks,
                StatusState = member.Status?.State,
                StatusUntilUtc = member.Status?.Until,
                CapturedAtUtc = capturedAtUtc
            }))
            .OrderBy(row => row.FactionId)
            .ThenBy(row => row.MemberId)
            .ToArray();

        var sample = new WarScoreSampleEntity
        {
            WarId = resolution.WarId,
            FactionId = ourFaction.FactionId,
            FactionName = ourFaction.Name,
            FactionScore = ourFaction.Score,
            FactionChain = ourFaction.Chain,
            OpponentFactionId = opponentFaction.FactionId,
            OpponentFactionName = opponentFaction.Name,
            OpponentScore = opponentFaction.Score,
            OpponentChain = opponentFaction.Chain,
            SampledAtUtc = capturedAtUtc,
            FactionChainLapsesAtUtc = ourChainLapsesAtUtc
        };

        return new PersistedWarState(current, rosterRows, sample);
    }

    private WarPollerHeartbeatEntity BuildHeartbeat(
        string phase,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? pollStartedAtUtc,
        DateTimeOffset? pollCompletedAtUtc,
        int retryCount,
        string? lastError,
        long? activeWarId,
        DateTimeOffset staleAfterUtc,
        int failureBackoffSeconds)
        => new()
        {
            ScopeKey = _options.ScopeKey,
            Phase = phase,
            UpdatedAtUtc = updatedAtUtc,
            PollStartedAtUtc = pollStartedAtUtc,
            PollCompletedAtUtc = pollCompletedAtUtc,
            RetryCount = retryCount,
            LastError = lastError,
            ActiveWarId = activeWarId,
            StaleAfterUtc = staleAfterUtc,
            PollIntervalSeconds = _options.PollIntervalSeconds,
            FailureBackoffSeconds = failureBackoffSeconds
        };

    /// <summary>
    /// Our faction's chain deadline, or null.
    ///
    /// One extra WarState-priority call per tick, on the same ~30 s cadence as the war report,
    /// so the rate budget grows by one request per poll and nothing is displaced. The chain
    /// selection reports the chain of the faction the key belongs to, so there is no opponent
    /// equivalent to fetch.
    ///
    /// Failures are swallowed on purpose. A missing deadline costs the exact countdown and falls
    /// back to the inferred timer; letting it throw would cost the whole tick — score, roster and
    /// holes included — for a strictly optional improvement. The war report is the poller's job;
    /// this is a garnish on it.
    /// </summary>
    private async Task<DateTimeOffset?> TryGetOurChainDeadlineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _tornApiClient.GetFactionChainAsync(_options.ApiKey, cancellationToken);
            return response.Chain?.LapsesAtUtc;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Chain deadline unavailable for faction {FactionId}; the board falls back to the inferred timer. {Reason}",
                _options.FactionId,
                BuildSanitizedErrorMessage(ex));
            return null;
        }
    }

    private async Task PersistHeartbeatAsync(WarPollerHeartbeatEntity heartbeat)
    {
        await _warStateRepository.UpsertHeartbeatAsync(heartbeat, CancellationToken.None);
        await _unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task TryNotifyHubAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.HubNotifyUrl))
        {
            return;
        }

        try
        {
            await _warPollerNotifier.NotifyWarStateUpdatedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "War poller hub notify timed out for scope {ScopeKey} endpoint {HubNotifyUrl} after {TimeoutSeconds}s.",
                _options.ScopeKey,
                DescribeHubNotifyEndpoint(),
                _options.HubNotifyTimeoutSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "War poller hub notify skipped during cancellation for scope {ScopeKey} endpoint {HubNotifyUrl}.",
                _options.ScopeKey,
                DescribeHubNotifyEndpoint());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "War poller hub notify failed for scope {ScopeKey} endpoint {HubNotifyUrl}. StatusCode={StatusCode} Error={Error}",
                _options.ScopeKey,
                DescribeHubNotifyEndpoint(),
                ex.StatusCode?.ToString() ?? "unknown",
                BuildSanitizedErrorMessage(ex));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "War poller hub notify failed unexpectedly for scope {ScopeKey} endpoint {HubNotifyUrl}. ExceptionType={ExceptionType} Error={Error}",
                _options.ScopeKey,
                DescribeHubNotifyEndpoint(),
                ex.GetType().Name,
                BuildSanitizedErrorMessage(ex));
        }
    }

    private string DescribeHubNotifyEndpoint()
    {
        if (!Uri.TryCreate(_options.HubNotifyUrl, UriKind.Absolute, out var uri))
        {
            return "invalid";
        }

        return uri.GetLeftPart(UriPartial.Path);
    }

    private TimeSpan ComputeFailureBackoff(int retryCount)
    {
        var multiplier = Math.Max(1, retryCount);
        var seconds = checked(_options.FailureBackoffSeconds * multiplier);
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxFailureBackoffSeconds));
    }

    private static string BuildSanitizedErrorMessage(Exception ex)
        => $"{ex.GetType().Name}: {SanitizeMessage(ex.Message)}";

    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error";
        }

        var sanitized = AbsoluteUrlRegex.Replace(message, "[redacted-url]");
        sanitized = ApiKeyRegex.Replace(sanitized, "$1[redacted]");
        return sanitized;
    }

    public sealed record WarPollerTickResult(string Phase, long? ActiveWarId, TimeSpan DelayBeforeNextTick, bool PersistedWarState);

    private sealed record ActiveWarResolution(long WarId, LiveFactionWar LiveWar);
    private sealed record PersistedWarState(WarCurrentEntity Current, IReadOnlyList<WarRosterSnapshotEntity> RosterRows, WarScoreSampleEntity ScoreSample);
}
