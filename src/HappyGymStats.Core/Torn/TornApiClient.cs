using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HappyGymStats.Core.Torn.Models;
using HappyGymStats.Core.War;

namespace HappyGymStats.Core.Torn;

public sealed record UserLogPage(IReadOnlyList<UserLog> Logs, Uri? NextUrl);

public sealed class TornApiException : Exception
{
    public TornApiException(
        string message,
        bool isRetryable,
        HttpStatusCode? statusCode,
        int? tornErrorCode,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsRetryable = isRetryable;
        StatusCode = statusCode;
        TornErrorCode = tornErrorCode;
    }

    /// <summary>
    /// True when the caller should consider retrying (e.g., rate limit, transient network/server error).
    /// </summary>
    public bool IsRetryable { get; }

    public HttpStatusCode? StatusCode { get; }
    public int? TornErrorCode { get; }
}

public sealed class TornApiClient
{
    private static readonly Uri TornApiBaseUri = new("https://api.torn.com/");
    private static readonly JsonSerializerOptions UserLogJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Regex AbsoluteUrlRegex = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly HttpClient _http;

    public TornApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<int> GetPlayerIdAsync(string apiKey, CancellationToken ct = default)
    {
        var requestUrl = new Uri(TornApiBaseUri, "v2/user/basic?selections=basic");

        return await GetAsync(
            apiKey,
            requestUrl,
            DeserializePlayerId,
            ct).ConfigureAwait(false);
    }

    public async Task<UserLogPage> GetUserLogPageAsync(string apiKey, Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        return await GetAsync(
            apiKey,
            url,
            DeserializeUserLogPage,
            ct).ConfigureAwait(false);
    }

    public Task<LiveFactionWarsResponse> GetLiveFactionWarsAsync(string apiKey, CancellationToken ct = default)
        => GetWarAsync<LiveFactionWarsResponse>(apiKey, new Uri(TornApiBaseUri, "faction/?selections=rankedwars"), ct);

    public Task<RankedWarReportResponse> GetRankedWarReportAsync(string apiKey, long warId, CancellationToken ct = default)
    {
        if (warId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warId), warId, "War id must be positive.");
        }

        return GetWarAsync<RankedWarReportResponse>(apiKey, new Uri(TornApiBaseUri, $"torn/{warId}?selections=rankedwarreport"), ct);
    }

    public Task<GlobalRankedWarsResponse> GetGlobalRankedWarsAsync(string apiKey, CancellationToken ct = default)
        => GetWarAsync<GlobalRankedWarsResponse>(apiKey, new Uri(TornApiBaseUri, "torn/?selections=rankedwars"), ct);

    public Task<UserAttacksPageResponse> GetUserAttacksPageAsync(string apiKey, CancellationToken ct = default)
        => GetUserAttacksPageAsync(apiKey, new Uri(TornApiBaseUri, "user/?selections=attacks"), ct);

    public Task<UserAttacksPageResponse> GetUserAttacksPageAsync(string apiKey, Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var normalizedUrl = NormalizeTornApiUri(url);
        return GetWarAsync<UserAttacksPageResponse>(apiKey, normalizedUrl, ct);
    }

    private Task<T> GetWarAsync<T>(string apiKey, Uri requestUrl, CancellationToken ct)
        where T : class
        => GetAsync(
            apiKey,
            requestUrl,
            root => DeserializeResponse<T>(root, WarEndpointJson.SerializerOptions, typeof(T).Name),
            ct);

    private async Task<T> GetAsync<T>(
        string apiKey,
        Uri requestUrl,
        Func<JsonElement, T> deserialize,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        ArgumentNullException.ThrowIfNull(requestUrl);

        requestUrl = BuildUrlWithApiKey(requestUrl, apiKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TornApiException(
                message: "Request timed out while calling Torn API.",
                isRetryable: true,
                statusCode: null,
                tornErrorCode: null);
        }
        catch (HttpRequestException ex)
        {
            throw new TornApiException(
                message: "Network error while calling Torn API.",
                isRetryable: true,
                statusCode: null,
                tornErrorCode: null,
                innerException: ex);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(stream, JsonDocumentOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            var retryable = IsRetryableStatusCode(response.StatusCode);
            throw new TornApiException(
                message: "Malformed JSON response from Torn API.",
                isRetryable: retryable,
                statusCode: response.StatusCode,
                tornErrorCode: null,
                innerException: ex);
        }

        using (doc)
        {
            if (TryGetTornError(doc.RootElement, out var tornErrorCode, out var tornErrorMessage))
            {
                var retryable = IsRetryableTornError(response.StatusCode, tornErrorCode, tornErrorMessage);
                throw new TornApiException(
                    message: BuildUserSafeErrorMessage(response.StatusCode, tornErrorCode, tornErrorMessage),
                    isRetryable: retryable,
                    statusCode: response.StatusCode,
                    tornErrorCode: tornErrorCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new TornApiException(
                    $"Torn API returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    isRetryable: IsRetryableStatusCode(response.StatusCode),
                    statusCode: response.StatusCode,
                    tornErrorCode: null);
            }

            try
            {
                return deserialize(doc.RootElement);
            }
            catch (JsonException ex)
            {
                throw new TornApiException(
                    message: "Malformed JSON response from Torn API.",
                    isRetryable: IsRetryableStatusCode(response.StatusCode),
                    statusCode: response.StatusCode,
                    tornErrorCode: null,
                    innerException: ex);
            }
        }
    }

    private static int DeserializePlayerId(JsonElement root)
    {
        if (!root.TryGetProperty("player_id", out var playerIdEl) || !playerIdEl.TryGetInt32(out var playerId))
        {
            throw new JsonException("Response did not contain a valid player_id.");
        }

        return playerId;
    }

    private static UserLogPage DeserializeUserLogPage(JsonElement root)
    {
        var page = DeserializeResponse<UserLogPageResponse>(root, UserLogJsonOptions, nameof(UserLogPageResponse));

        if (page.Logs.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Response did not contain a valid logs object.");
        }

        var logs = new List<UserLog>();
        foreach (var prop in page.Logs.EnumerateObject())
        {
            var logEl = prop.Value;
            var id = logEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? ""
                : idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt64(out var idNum)
                    ? idNum.ToString()
                    : "";
            var ts = logEl.TryGetProperty("timestamp", out var tsEl) && tsEl.TryGetInt64(out var tsVal) ? tsVal : 0;

            string? title = null;
            string? category = null;
            int? logTypeId = null;

            if (logEl.TryGetProperty("details", out var detailsEl) && detailsEl.ValueKind == JsonValueKind.Object)
            {
                if (detailsEl.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                {
                    title = titleEl.GetString();
                }

                if (detailsEl.TryGetProperty("category", out var categoryEl) && categoryEl.ValueKind == JsonValueKind.String)
                {
                    category = categoryEl.GetString();
                }

                if (detailsEl.TryGetProperty("id", out var idPropEl) && idPropEl.TryGetInt32(out var idInt))
                {
                    logTypeId = idInt;
                }
            }

            logs.Add(new UserLog(
                Id: id,
                Timestamp: ts,
                Title: title,
                Category: category,
                LogTypeId: logTypeId,
                Raw: logEl.Clone()));
        }

        Uri? nextUrl = null;
        var next = page.Metadata?.Links?.Next;
        if (!string.IsNullOrWhiteSpace(next) && Uri.TryCreate(next, UriKind.Absolute, out var abs))
        {
            nextUrl = abs;
        }

        return new UserLogPage(logs, nextUrl);
    }

    private static T DeserializeResponse<T>(JsonElement root, JsonSerializerOptions options, string responseTypeName)
    {
        return root.Deserialize<T>(options)
            ?? throw new JsonException($"Response body deserialized to null for {responseTypeName}.");
    }

    private static Uri NormalizeTornApiUri(Uri uri)
    {
        if (uri.IsAbsoluteUri)
        {
            return uri;
        }

        var pathAndQuery = uri.OriginalString;
        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return new Uri(TornApiBaseUri, pathAndQuery);
    }

    private static Uri BuildUrlWithApiKey(Uri baseUrl, string apiKey)
    {
        var ub = new UriBuilder(baseUrl);
        var existing = ub.Query?.TrimStart('?');
        var pairs = new List<string>();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            foreach (var kv in existing.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = kv.Split('=', 2)[0];
                if (!key.Equals("key", StringComparison.OrdinalIgnoreCase))
                {
                    pairs.Add(kv);
                }
            }
        }

        pairs.Add($"key={Uri.EscapeDataString(apiKey)}");
        ub.Query = string.Join('&', pairs);
        return ub.Uri;
    }

    private static bool TryGetTornError(JsonElement root, out int code, out string? errorMessage)
    {
        code = 0;
        errorMessage = null;
        if (!root.TryGetProperty("error", out var errorEl) || errorEl.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (errorEl.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var c))
        {
            code = c;
        }

        if (errorEl.TryGetProperty("error", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
        {
            errorMessage = msgEl.GetString();
        }

        return true;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static bool IsRetryableTornError(HttpStatusCode statusCode, int tornErrorCode, string? message)
        => IsRetryableStatusCode(statusCode)
           || tornErrorCode == 5
           || (message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string BuildUserSafeErrorMessage(HttpStatusCode statusCode, int tornErrorCode, string? tornError)
    {
        var baseMessage = $"Torn API error {tornErrorCode}";
        if (statusCode != HttpStatusCode.OK)
        {
            baseMessage += $" with HTTP {(int)statusCode} ({statusCode})";
        }

        var sanitized = SanitizeErrorText(tornError);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            baseMessage += $": {sanitized}";
        }

        return baseMessage + ".";
    }

    private static string? SanitizeErrorText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var scrubbed = AbsoluteUrlRegex.Replace(input, "[redacted-url]");
        scrubbed = Regex.Replace(scrubbed, @"(?i)(^|[?&])key=[^&\s]+", "$1key=[redacted]");
        return scrubbed;
    }
}
