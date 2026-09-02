using System.Net;
using System.Text.RegularExpressions;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.WarPoller;

/// <summary>
/// Runs one bounded unit of resumable ranked-war history backfill work: fetch the current
/// <c>warfareranked</c> history page from persisted cursor state, persist it through
/// <see cref="IWarHistoryIngestWriter"/>, then fetch and persist any not-yet-captured
/// <c>rankedwarreport</c> payloads for wars on that page. Never advances Torn calls when the
/// backfill is disabled or waiting out a retry backoff.
/// </summary>
public sealed class RankedWarHistoryBackfillWorker(
    TornApiClient tornApiClient,
    IWarHistoryRepository warHistoryRepository,
    IWarHistoryIngestWriter ingestWriter,
    IRankedWarHistoryBackfillStateRepository stateRepository,
    IUnitOfWork unitOfWork,
    WarPollerOptions options,
    IWarPollerClock clock,
    ILogger<RankedWarHistoryBackfillWorker> logger)
{
    private static readonly Regex AbsoluteUrlRegex = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApiKeyRegex = new(@"([?&]key=)[^&\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<RankedWarHistoryBackfillIterationResult> RunIterationAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var state = await LoadOrCreateStateAsync(now);

        if (string.Equals(state.Status, RankedWarHistoryBackfillStatus.Completed, StringComparison.Ordinal))
        {
            return new RankedWarHistoryBackfillIterationResult(
                state.Status,
                state.Phase ?? RankedWarHistoryBackfillPhase.Idle,
                TimeSpan.FromSeconds(options.RankedWarHistoryBackfillIterationDelaySeconds),
                null);
        }

        if (state.NextRetryAtUtc is { } nextRetryAtUtc && nextRetryAtUtc > now)
        {
            return new RankedWarHistoryBackfillIterationResult(
                state.Status,
                state.Phase ?? RankedWarHistoryBackfillPhase.Idle,
                nextRetryAtUtc - now,
                null);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pagesThisIteration = 0;
            var reportBudget = options.RankedWarHistoryBackfillMaxReportsPerIteration;

            while (pagesThisIteration < options.RankedWarHistoryBackfillMaxPagesPerIteration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                state.Status = RankedWarHistoryBackfillStatus.Running;
                state.Phase = RankedWarHistoryBackfillPhase.FetchingHistoryPage;
                await PersistStateAsync(state, clock.UtcNow);

                var page = state.NextHistoryPageUrl is null
                    ? await tornApiClient.GetRankedWarHistoryPageAsync(options.ApiKey, cancellationToken)
                    : await tornApiClient.GetRankedWarHistoryPageAsync(
                        options.ApiKey,
                        new Uri(state.NextHistoryPageUrl, UriKind.RelativeOrAbsolute),
                        cancellationToken);

                var pageCapturedAtUtc = clock.UtcNow;
                await ingestWriter.WriteHistoryPageAsync(page, pageCapturedAtUtc, pageCapturedAtUtc, cancellationToken);

                state.Phase = RankedWarHistoryBackfillPhase.FetchingReport;
                var pageDrained = true;

                foreach (var war in page.Wars)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await warHistoryRepository.HasCapturedReportAsync(war.WarId, CancellationToken.None))
                    {
                        continue;
                    }

                    if (reportBudget <= 0)
                    {
                        pageDrained = false;
                        break;
                    }

                    var report = await tornApiClient.GetRankedWarReportAsync(options.ApiKey, war.WarId, cancellationToken);
                    var reportCapturedAtUtc = clock.UtcNow;
                    await ingestWriter.WriteReportAsync(report, reportCapturedAtUtc, reportCapturedAtUtc, cancellationToken);

                    reportBudget--;
                    state.LastProcessedWarId = war.WarId;
                    state.ReportsProcessed++;
                    await PersistStateAsync(state, clock.UtcNow);
                }

                if (!pageDrained)
                {
                    // Leave NextHistoryPageUrl pointing at this same page; remaining wars still
                    // without a captured report are retried next iteration, and already-captured
                    // reports are skipped idempotently via HasCapturedReportAsync.
                    break;
                }

                state.PagesProcessed++;
                state.NextHistoryPageUrl = page.Metadata?.Links?.Next;
                pagesThisIteration++;

                if (string.IsNullOrWhiteSpace(state.NextHistoryPageUrl))
                {
                    state.Status = RankedWarHistoryBackfillStatus.Completed;
                    state.Phase = RankedWarHistoryBackfillPhase.Idle;
                    await PersistSuccessAsync(state, clock.UtcNow);

                    logger.LogInformation(
                        "Ranked-war history backfill for scope {ScopeKey} reached the last history page; backfill complete.",
                        options.RankedWarHistoryBackfillScopeKey);

                    return new RankedWarHistoryBackfillIterationResult(
                        state.Status,
                        state.Phase,
                        TimeSpan.FromSeconds(options.RankedWarHistoryBackfillIterationDelaySeconds),
                        null);
                }
            }

            state.Phase = RankedWarHistoryBackfillPhase.Idle;
            await PersistSuccessAsync(state, clock.UtcNow);

            return new RankedWarHistoryBackfillIterationResult(
                state.Status,
                state.Phase,
                TimeSpan.FromSeconds(options.RankedWarHistoryBackfillIterationDelaySeconds),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var category = ClassifyFailure(ex);
            var failedAtUtc = clock.UtcNow;
            var retryCount = state.RetryCount + 1;
            var backoff = ComputeFailureBackoff(retryCount);
            var sanitizedMessage = SanitizeMessage(ex.Message);

            state.Status = RankedWarHistoryBackfillStatus.WaitingRetry;
            state.RetryCount = retryCount;
            state.LastFailureCategory = category.ToString();
            state.LastErrorMessage = sanitizedMessage;
            state.LastFailureAtUtc = failedAtUtc;
            state.NextRetryAtUtc = failedAtUtc.Add(backoff);
            await PersistStateAsync(state, failedAtUtc);

            logger.LogWarning(
                "Ranked-war history backfill hit a {Category} failure for scope {ScopeKey}; retrying in {BackoffSeconds}s. Error={Error}",
                category,
                options.RankedWarHistoryBackfillScopeKey,
                (int)backoff.TotalSeconds,
                sanitizedMessage);

            return new RankedWarHistoryBackfillIterationResult(state.Status, state.Phase ?? RankedWarHistoryBackfillPhase.Idle, backoff, category);
        }
    }

    private async Task<RankedWarHistoryBackfillStateEntity> LoadOrCreateStateAsync(DateTimeOffset now)
    {
        var existing = await stateRepository.GetAsync(options.RankedWarHistoryBackfillScopeKey, CancellationToken.None);
        if (existing is not null)
        {
            return existing;
        }

        return new RankedWarHistoryBackfillStateEntity
        {
            ScopeKey = options.RankedWarHistoryBackfillScopeKey,
            Status = RankedWarHistoryBackfillStatus.NotStarted,
            Phase = RankedWarHistoryBackfillPhase.Idle,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private async Task PersistSuccessAsync(RankedWarHistoryBackfillStateEntity state, DateTimeOffset now)
    {
        state.RetryCount = 0;
        state.LastFailureCategory = null;
        state.LastErrorMessage = null;
        state.NextRetryAtUtc = null;
        state.LastSuccessAtUtc = now;
        await PersistStateAsync(state, now);
    }

    private async Task PersistStateAsync(RankedWarHistoryBackfillStateEntity state, DateTimeOffset now)
    {
        state.UpdatedAtUtc = now;
        await stateRepository.UpsertAsync(state, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private TimeSpan ComputeFailureBackoff(int retryCount)
    {
        var multiplier = Math.Max(1, retryCount);
        var seconds = checked(options.RankedWarHistoryBackfillFailureBackoffSeconds * multiplier);
        return TimeSpan.FromSeconds(Math.Min(seconds, options.RankedWarHistoryBackfillMaxFailureBackoffSeconds));
    }

    private static RankedWarHistoryBackfillFailureCategory ClassifyFailure(Exception ex)
    {
        if (ex is TornApiException tornEx)
        {
            if (tornEx.StatusCode == HttpStatusCode.TooManyRequests || tornEx.TornErrorCode == 5)
            {
                return RankedWarHistoryBackfillFailureCategory.RateLimited;
            }

            return tornEx.IsRetryable
                ? RankedWarHistoryBackfillFailureCategory.TransientHttp
                : RankedWarHistoryBackfillFailureCategory.MalformedResponse;
        }

        if (ex is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            return RankedWarHistoryBackfillFailureCategory.IngestValidation;
        }

        return RankedWarHistoryBackfillFailureCategory.Unexpected;
    }

    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error";
        }

        var sanitized = AbsoluteUrlRegex.Replace(message, "[redacted-url]");
        sanitized = ApiKeyRegex.Replace(sanitized, "$1[redacted]");
        return sanitized.Length > 512 ? sanitized[..512] : sanitized;
    }
}
