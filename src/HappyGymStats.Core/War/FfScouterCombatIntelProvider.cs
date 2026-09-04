using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyGymStats.Core.War;

public enum CombatIntelProviderFetchStatus
{
    Available = 0,
    Partial = 1,
    Unavailable = 2,
}

public sealed record CombatIntelProviderFetchResult(
    CombatIntelProviderFetchStatus Status,
    IReadOnlyList<CombatIntelObservation> Observations,
    IReadOnlyList<long> MissingPlayerIds,
    string? FailureCode = null);

public sealed class FfScouterCombatIntelProvider
{
    public const int MaxTargetsPerRequest = 205;
    public static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultMinimumRequestInterval = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cacheLifetime;
    private readonly TimeSpan _minimumRequestInterval;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastRequestAtUtc;

    public FfScouterCombatIntelProvider(
        HttpClient httpClient,
        string apiKey,
        Uri? baseUri = null,
        TimeProvider? timeProvider = null,
        TimeSpan? cacheLifetime = null,
        TimeSpan? minimumRequestInterval = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("FFScouter API key must be non-empty.", nameof(apiKey));
        }

        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUri = baseUri ?? new Uri("https://ffscouter.com/", UriKind.Absolute);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cacheLifetime = cacheLifetime ?? DefaultCacheLifetime;
        _minimumRequestInterval = minimumRequestInterval ?? DefaultMinimumRequestInterval;

        if (_cacheLifetime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheLifetime));
        }
        if (_minimumRequestInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRequestInterval));
        }
    }

    public async Task<CombatIntelProviderFetchResult> FetchAsync(
        IEnumerable<long> playerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        var ids = playerIds.Distinct().Order().ToArray();
        if (ids.Length == 0 || ids.Any(id => id <= 0))
        {
            throw new ArgumentException("At least one positive player id is required.", nameof(playerIds));
        }

        var cacheKey = string.Join(',', ids);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_cache.TryGetValue(cacheKey, out var cached) && now - cached.StoredAtUtc <= _cacheLifetime)
            {
                return cached.Result;
            }

            var observations = new List<CombatIntelObservation>();
            var missing = new HashSet<long>(ids);
            var hadProviderFailure = false;

            foreach (var batch in ids.Chunk(MaxTargetsPerRequest))
            {
                if (_lastRequestAtUtc.HasValue)
                {
                    var remaining = _minimumRequestInterval - (now - _lastRequestAtUtc.Value);
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, _timeProvider, cancellationToken).ConfigureAwait(false);
                        now = _timeProvider.GetUtcNow();
                    }
                }

                var response = await FetchBatchAsync(batch, now, cancellationToken).ConfigureAwait(false);
                _lastRequestAtUtc = _timeProvider.GetUtcNow();
                if (response is null)
                {
                    hadProviderFailure = true;
                    continue;
                }

                foreach (var row in response)
                {
                    if (!missing.Contains(row.PlayerId) || !TryTranslate(row, now, out var observation))
                    {
                        continue;
                    }

                    observations.Add(observation);
                    missing.Remove(row.PlayerId);
                }
            }

            var status = observations.Count == 0 && hadProviderFailure
                ? CombatIntelProviderFetchStatus.Unavailable
                : missing.Count == 0
                    ? CombatIntelProviderFetchStatus.Available
                    : CombatIntelProviderFetchStatus.Partial;
            var result = new CombatIntelProviderFetchResult(
                status,
                observations,
                missing.Order().ToArray(),
                hadProviderFailure ? "ffscouter_unavailable" : null);

            if (!hadProviderFailure)
            {
                _cache[cacheKey] = new CacheEntry(now, result);
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FfScouterRow[]?> FetchBatchAsync(
        IReadOnlyCollection<long> playerIds,
        DateTimeOffset trustedNow,
        CancellationToken cancellationToken)
    {
        var targets = string.Join(',', playerIds);
        var relative = $"api/v1/get-stats?key={Uri.EscapeDataString(_apiKey)}&targets={Uri.EscapeDataString(targets)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relative));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<FfScouterRow[]>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool TryTranslate(
        FfScouterRow row,
        DateTimeOffset fetchedAtUtc,
        out CombatIntelObservation observation)
    {
        observation = null!;
        if (row.PlayerId <= 0 || !row.BattleStats.HasValue || row.BattleStats.Value < 0 || !row.LastUpdated.HasValue)
        {
            return false;
        }

        // FFScouter's public BSS value is explicitly an estimate without uncertainty bounds.
        // The neutral model requires honest bounds for Estimated observations, so do not turn
        // a point estimate into fake certainty. Premium/faction-spy winners are direct spy totals.
        if (!string.Equals(row.Source, "premium", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.Source, "spies", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        DateTimeOffset observedAtUtc;
        try
        {
            observedAtUtc = DateTimeOffset.FromUnixTimeSeconds(row.LastUpdated.Value);
            observation = CombatIntelObservation.CreateFromProvider(
                $"ffscouter:{row.PlayerId}:{row.LastUpdated.Value}:{row.Source!.ToLowerInvariant()}",
                row.PlayerId,
                "ffscouter",
                fetchedAtUtc,
                observedAtUtc,
                fetchedAtUtc,
                CombatIntelClassification.Exact,
                value: row.BattleStats.Value,
                providerMetadata: $"source={row.Source!.ToLowerInvariant()}");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed record CacheEntry(DateTimeOffset StoredAtUtc, CombatIntelProviderFetchResult Result);

    private sealed record FfScouterRow
    {
        [JsonPropertyName("player_id")]
        public long PlayerId { get; init; }

        [JsonPropertyName("bs_estimate")]
        public decimal? BattleStats { get; init; }

        [JsonPropertyName("last_updated")]
        public long? LastUpdated { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }
    }
}
