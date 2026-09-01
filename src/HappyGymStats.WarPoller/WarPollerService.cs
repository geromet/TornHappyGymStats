using System.IO;
using System.Text.RegularExpressions;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.WarPoller;

public sealed class WarPollerService
{
    private static readonly Regex AbsoluteUrlRegex = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApiKeyRegex = new(@"([?&]key=)[^&\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TornApiClient _tornApiClient;
    private readonly IWarStateRepository _warStateRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly WarPollerOptions _options;
    private readonly IWarPollerClock _clock;
    private readonly ILogger<WarPollerService> _logger;

    public WarPollerService(
        TornApiClient tornApiClient,
        IWarStateRepository warStateRepository,
        IImportRunRepository importRunRepository,
        IUnitOfWork unitOfWork,
        WarPollerOptions options,
        IWarPollerClock clock,
        ILogger<WarPollerService> logger)
    {
        _tornApiClient = tornApiClient ?? throw new ArgumentNullException(nameof(tornApiClient));
        _warStateRepository = warStateRepository ?? throw new ArgumentNullException(nameof(warStateRepository));
        ArgumentNullException.ThrowIfNull(importRunRepository);
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _options.Validate();
    }

    public async Task<WarPollerTickResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var heartbeat = await _warStateRepository.GetHeartbeatAsync(_options.ScopeKey, CancellationToken.None);
        var activeWarId = heartbeat?.ActiveWarId;
        var now = _clock.UtcNow;

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

        var pollStartedAtUtc = _clock.UtcNow;
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
            if (resolution is null)
            {
                var observedAtUtc = _clock.UtcNow;
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

                var noWarCompletedAtUtc = _clock.UtcNow;
                await PersistHeartbeatAsync(BuildHeartbeat(
                    phase: "succeeded",
                    updatedAtUtc: noWarCompletedAtUtc,
                    pollStartedAtUtc: pollStartedAtUtc,
                    pollCompletedAtUtc: noWarCompletedAtUtc,
                    retryCount: 0,
                    lastError: null,
                    activeWarId: null,
                    staleAfterUtc: noWarCompletedAtUtc.AddSeconds(_options.PollIntervalSeconds),
                    failureBackoffSeconds: _options.FailureBackoffSeconds));

                _logger.LogInformation("War poller found no active war for scope {ScopeKey} faction {FactionId}.", _options.ScopeKey, _options.FactionId);
                return new WarPollerTickResult("succeeded", null, TimeSpan.FromSeconds(_options.PollIntervalSeconds), false);
            }

            var report = await _tornApiClient.GetRankedWarReportAsync(_options.ApiKey, resolution.WarId, cancellationToken);
            var capturedAtUtc = _clock.UtcNow;
            var persistedState = BuildPersistedState(resolution, report, capturedAtUtc);

            await _warStateRepository.UpsertCurrentAsync(persistedState.Current, cancellationToken);
            await _warStateRepository.ReplaceRosterSnapshotAsync(persistedState.Current.WarId!.Value, persistedState.RosterRows, cancellationToken);
            await _warStateRepository.AddScoreSampleAsync(persistedState.ScoreSample, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var completedAtUtc = _clock.UtcNow;
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

            _logger.LogInformation("War poller persisted active war {WarId} for scope {ScopeKey}.", resolution.WarId, _options.ScopeKey);
            return new WarPollerTickResult("succeeded", resolution.WarId, TimeSpan.FromSeconds(_options.PollIntervalSeconds), true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelledAtUtc = _clock.UtcNow;
            await PersistHeartbeatAsync(BuildHeartbeat(
                phase: "cancelled",
                updatedAtUtc: cancelledAtUtc,
                pollStartedAtUtc: pollStartedAtUtc,
                pollCompletedAtUtc: cancelledAtUtc,
                retryCount: heartbeat?.RetryCount ?? 0,
                lastError: "poll cancelled",
                activeWarId: activeWarId,
                staleAfterUtc: cancelledAtUtc.AddSeconds(_options.PollIntervalSeconds),
                failureBackoffSeconds: _options.FailureBackoffSeconds));

            _logger.LogInformation("War poller cancelled for scope {ScopeKey}.", _options.ScopeKey);
            return new WarPollerTickResult("cancelled", activeWarId, TimeSpan.Zero, false);
        }
        catch (TornApiException ex) when (ex.IsRetryable)
        {
            var retryCount = (heartbeat?.RetryCount ?? 0) + 1;
            var backoff = ComputeFailureBackoff(retryCount);
            var failedAtUtc = _clock.UtcNow;
            var sanitizedMessage = BuildSanitizedErrorMessage(ex);

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
                await _clock.DelayAsync(backoff, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var cancelledAtUtc = _clock.UtcNow;
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
        catch (Exception ex)
        {
            var failedAtUtc = _clock.UtcNow;
            var sanitizedMessage = BuildSanitizedErrorMessage(ex);

            await PersistHeartbeatAsync(BuildHeartbeat(
                phase: "failed",
                updatedAtUtc: failedAtUtc,
                pollStartedAtUtc: pollStartedAtUtc,
                pollCompletedAtUtc: failedAtUtc,
                retryCount: heartbeat?.RetryCount ?? 0,
                lastError: sanitizedMessage,
                activeWarId: activeWarId,
                staleAfterUtc: failedAtUtc.AddSeconds(_options.PollIntervalSeconds),
                failureBackoffSeconds: _options.FailureBackoffSeconds));
            _logger.LogError(
                "War poller failed for scope {ScopeKey}. ExceptionType={ExceptionType} Error={Error}",
                _options.ScopeKey,
                ex.GetType().Name,
                sanitizedMessage);

            throw;
        }
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

    private PersistedWarState BuildPersistedState(ActiveWarResolution resolution, RankedWarReportResponse report, DateTimeOffset capturedAtUtc)
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
            SampledAtUtc = capturedAtUtc
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

    private async Task PersistHeartbeatAsync(WarPollerHeartbeatEntity heartbeat)
    {
        await _warStateRepository.UpsertHeartbeatAsync(heartbeat, CancellationToken.None);
        await _unitOfWork.SaveChangesAsync(CancellationToken.None);
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
