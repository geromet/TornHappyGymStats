using System.Globalization;
using System.Text.Json;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.Torn.Models;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Fetch;

public enum FetchMode
{
    Fresh,
    Resume,
}

public sealed record FetchRunResult(
    int PagesFetched,
    long LogsFetched,
    long LogsAppended);

public sealed class LogFetcher
{
    private readonly IUserLogEntryRepository _userLogRepo;
    private readonly IImportRunRepository _importRunRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TornApiClient _client;

    public LogFetcher(IUserLogEntryRepository userLogRepo, IImportRunRepository importRunRepo, IUnitOfWork unitOfWork, TornApiClient client)
    {
        _userLogRepo = userLogRepo ?? throw new ArgumentNullException(nameof(userLogRepo));
        _importRunRepo = importRunRepo ?? throw new ArgumentNullException(nameof(importRunRepo));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<FetchRunResult> RunAsync(
        string apiKey,
        int playerId,
        FetchMode mode,
        FetchOptions options,
        CancellationToken ct,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must be provided.", nameof(apiKey));

        if (options is null)
            throw new ArgumentNullException(nameof(options));

        // Build a dedup set from existing entries for this player.
        var existingIds = await _userLogRepo.GetExistingLogIdsAsync(playerId, ct);

        Uri? nextUrl;
        ImportRunEntity importRun;
        var now = DateTimeOffset.UtcNow;

        if (mode == FetchMode.Fresh)
        {
            nextUrl = options.FreshStartUrl;
            importRun = new ImportRunEntity
            {
                PlayerId = playerId,
                StartedAtUtc = now,
                Outcome = "running",
                NextUrl = nextUrl.ToString(),
            };
            await _importRunRepo.CreateAsync(importRun, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else
        {
            // Resume: find latest incomplete run for this player that has a NextUrl.
            var priorRun = await _importRunRepo.GetLatestIncompleteAsync(playerId, ct);

            if (priorRun is null || string.IsNullOrWhiteSpace(priorRun.NextUrl))
            {
                log?.Invoke("No incomplete run with a resume cursor found. Nothing to resume.");
                return new FetchRunResult(PagesFetched: 0, LogsFetched: 0, LogsAppended: 0);
            }

            if (!Uri.TryCreate(priorRun.NextUrl, UriKind.Absolute, out var parsedNext))
            {
                throw new InvalidDataException($"Resume NextUrl is not a valid absolute URI: '{priorRun.NextUrl}'.");
            }

            nextUrl = parsedNext;

            importRun = new ImportRunEntity
            {
                PlayerId = playerId,
                StartedAtUtc = now,
                Outcome = "running",
                NextUrl = priorRun.NextUrl,
            };
            await _importRunRepo.CreateAsync(importRun, ct);
            await _unitOfWork.SaveChangesAsync(ct);
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

                var newEntries = new List<UserLogEntryEntity>(page.Logs.Count);

                foreach (var entry in page.Logs)
                {
                    if (string.IsNullOrEmpty(entry.Id))
                        continue;

                    if (!existingIds.Add(entry.Id))
                        continue;

                    newEntries.Add(MapUserLogEntry(entry, playerId));
                }

                if (newEntries.Count > 0)
                {
                    await _userLogRepo.AddRangeAsync(newEntries, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                    logsAppended += newEntries.Count;
                }

                importRun.NextUrl = page.NextUrl?.OriginalString;
                importRun.PagesFetched = pagesFetched;
                importRun.LogsFetched = logsFetched;
                importRun.LogsAppended = logsAppended;
                await _importRunRepo.UpdateAsync(importRun, ct); // semantic no-op; entity is tracked
                await _unitOfWork.SaveChangesAsync(ct);

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

                log?.Invoke($"Page {pagesFetched}: fetched={page.Logs.Count}, appended={newEntries.Count}, next=present");
                nextUrl = page.NextUrl;
            }

            importRun.CompletedAtUtc = DateTimeOffset.UtcNow;
            importRun.Outcome = "completed";
            importRun.NextUrl = null;
            importRun.PagesFetched = pagesFetched;
            importRun.LogsFetched = logsFetched;
            importRun.LogsAppended = logsAppended;
            await _importRunRepo.UpdateAsync(importRun, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return new FetchRunResult(pagesFetched, logsFetched, logsAppended);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            importRun.CompletedAtUtc = DateTimeOffset.UtcNow;
            importRun.Outcome = "cancelled";
            importRun.PagesFetched = pagesFetched;
            importRun.LogsFetched = logsFetched;
            importRun.LogsAppended = logsAppended;
            await _importRunRepo.UpdateAsync(importRun, ct);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            importRun.CompletedAtUtc = DateTimeOffset.UtcNow;
            importRun.Outcome = "failed";
            importRun.ErrorMessage = ex.Message;
            importRun.PagesFetched = pagesFetched;
            importRun.LogsFetched = logsFetched;
            importRun.LogsAppended = logsAppended;
            await _importRunRepo.UpdateAsync(importRun, ct);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
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

    private static UserLogEntryEntity MapUserLogEntry(UserLog entry, int playerId)
    {
        var raw = entry.Raw;
        JsonElement data = default;
        var hasData = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("data", out data);

        return new UserLogEntryEntity
        {
            PlayerId = playerId,
            LogEntryId = entry.Id,
            OccurredAtUtc = SafeUnixSeconds(entry.Timestamp) ?? DateTimeOffset.UnixEpoch,
            LogTypeId = entry.LogTypeId ?? 0,
            HappyBeforeApi = hasData ? TryGetInt(data, "happy_before") : null,
            HappyUsed = hasData ? TryGetInt(data, "happy_used") : null,
            HappyIncreased = hasData ? TryGetInt(data, "happy_increased") : null,
            HappyDecreased = hasData ? TryGetInt(data, "happy_decreased") : null,
            EnergyUsed = hasData ? TryGetDouble(data, "energy_used") : null,
            StrengthBefore = hasData ? TryGetDouble(data, "strength_before") : null,
            StrengthIncreased = hasData ? TryGetDouble(data, "strength_increased") : null,
            DefenseBefore = hasData ? TryGetDouble(data, "defense_before") : null,
            DefenseIncreased = hasData ? TryGetDouble(data, "defense_increased") : null,
            SpeedBefore = hasData ? TryGetDouble(data, "speed_before") : null,
            SpeedIncreased = hasData ? TryGetDouble(data, "speed_increased") : null,
            DexterityBefore = hasData ? TryGetDouble(data, "dexterity_before") : null,
            DexterityIncreased = hasData ? TryGetDouble(data, "dexterity_increased") : null,
            MaxHappyBefore = hasData ? TryGetInt(data, "maximum_happy_before") : null,
            MaxHappyAfter = hasData ? TryGetInt(data, "maximum_happy_after") : null,
        };
    }

    private static double? TryGetDouble(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static int? TryGetInt(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var el)) return null;
        if (el.TryGetInt32(out var i)) return i;
        if (el.TryGetDouble(out var d)) return (int)d;
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return (int)s;
        return null;
    }

    private static DateTimeOffset? SafeUnixSeconds(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return null;

        try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch { return null; }
    }
}
