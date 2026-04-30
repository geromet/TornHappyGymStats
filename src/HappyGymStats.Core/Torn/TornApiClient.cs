using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using HappyGymStats.Torn.Models;

namespace HappyGymStats.Torn;

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
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HttpClient _http;

    public TornApiClient(HttpClient httpClient)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<UserLogPage> GetUserLogPageAsync(string apiKey, Uri url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must be provided.", nameof(apiKey));
        }

        if (url is null)
        {
            throw new ArgumentNullException(nameof(url));
        }

        if (!url.IsAbsoluteUri)
        {
            throw new ArgumentException("URL must be an absolute URI.", nameof(url));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout (or other non-user cancellation).
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
                var retryable = IsRetryableStatusCode(response.StatusCode);
                throw new TornApiException(
                    message: $"Torn API returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    isRetryable: retryable,
                    statusCode: response.StatusCode,
                    tornErrorCode: null);
            }

            if (!doc.RootElement.TryGetProperty("log", out var logsElement) || logsElement.ValueKind != JsonValueKind.Array)
            {
                // Include HTTP status and response snippet so the user can diagnose what happened.
                var snippet = doc.RootElement.ValueKind == JsonValueKind.Object
                    ? JsonSerializer.Serialize(doc.RootElement)
                    : $"(root is {doc.RootElement.ValueKind})";
                if (snippet.Length > 500)
                    snippet = snippet[..500] + "...(truncated)";

                throw new TornApiException(
                    message: $"Torn API returned HTTP {(int)response.StatusCode} ({response.StatusCode}) with no 'log' array. Response: {snippet}",
                    isRetryable: IsRetryableStatusCode(response.StatusCode),
                    statusCode: response.StatusCode,
                    tornErrorCode: null);
            }

            if (!TryGetNextUrl(doc.RootElement, out var nextUrl, out var nextUrlError))
            {
                throw new TornApiException(
                    message: nextUrlError ?? "Malformed response from Torn API: invalid paging metadata.",
                    isRetryable: true,
                    statusCode: response.StatusCode,
                    tornErrorCode: null);
            }

            var logs = new List<UserLog>();
            foreach (var logEl in logsElement.EnumerateArray())
            {
                if (logEl.ValueKind != JsonValueKind.Object)
                {
                    continue; // ignore unexpected elements
                }

                var id = logEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString() ?? ""
                    : idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt64(out var idNum)
                        ? idNum.ToString()
                        : "";
                var ts = logEl.TryGetProperty("timestamp", out var tsEl) && tsEl.TryGetInt64(out var tsVal) ? tsVal : 0;

                string? title = null;
                string? category = null;

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
                }

                logs.Add(new UserLog(
                    Id: id,
                    Timestamp: ts,
                    Title: title,
                    Category: category,
                    Raw: logEl.Clone()));
            }

            return new UserLogPage(logs, nextUrl);
        }
    }

    private static bool TryGetNextUrl(JsonElement root, out Uri? nextUrl, out string? error)
    {
        nextUrl = null;
        error = null;

        if (!root.TryGetProperty("_metadata", out var mdEl) || mdEl.ValueKind != JsonValueKind.Object)
        {
            error = "Malformed response from Torn API: missing '_metadata'.";
            return false;
        }

        if (!mdEl.TryGetProperty("links", out var linksEl) || linksEl.ValueKind != JsonValueKind.Object)
        {
            error = "Malformed response from Torn API: missing '_metadata.links'.";
            return false;
        }

        // Torn API returns logs newest-first. The "prev" link goes to older records (backward in time),
        // which is what we want for fetching full history. The "next" link goes to newer records.
        // We prefer "prev" for backward paging; fall back to "next" if no "prev" exists.
        if (linksEl.TryGetProperty("prev", out var prevEl))
        {
            if (prevEl.ValueKind == JsonValueKind.Null || prevEl.ValueKind != JsonValueKind.String)
            {
                // No prev link = terminal page (reached oldest records).
                return true;
            }

            var prevStr = prevEl.GetString();
            if (string.IsNullOrWhiteSpace(prevStr))
            {
                return true;
            }

            if (!Uri.TryCreate(prevStr, UriKind.Absolute, out var parsedPrev)
                || (parsedPrev.Scheme != Uri.UriSchemeHttp && parsedPrev.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(parsedPrev.Host))
            {
                error = "Malformed response from Torn API: '_metadata.links.prev' is not a valid absolute HTTP(S) URI.";
                return false;
            }

            nextUrl = parsedPrev;
            return true;
        }

        // Fallback: use "next" if no "prev" exists.
        if (!linksEl.TryGetProperty("next", out var nextEl))
        {
            // No next link is a valid terminal page.
            return true;
        }

        if (nextEl.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (nextEl.ValueKind != JsonValueKind.String)
        {
            error = "Malformed response from Torn API: '_metadata.links.next' is not a string.";
            return false;
        }

        var nextStr = nextEl.GetString();
        if (string.IsNullOrWhiteSpace(nextStr))
        {
            return true;
        }

        if (!Uri.TryCreate(nextStr, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(parsed.Host))
        {
            error = "Malformed response from Torn API: '_metadata.links.next' is not a valid absolute HTTP(S) URI.";
            return false;
        }

        nextUrl = parsed;
        return true;
    }

    private static bool TryGetTornError(JsonElement root, out int? code, out string? error)
    {
        code = null;
        error = null;

        // Torn-style error payloads are usually: { "code": 2, "error": "Incorrect key" }
        if (!root.TryGetProperty("error", out var errorEl) || errorEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        error = errorEl.GetString();

        if (root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var parsedCode))
        {
            code = parsedCode;
        }

        return true;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        var sc = (int)statusCode;
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        // Server-side/transient.
        return sc >= 500 || statusCode == HttpStatusCode.RequestTimeout;
    }

    private static bool IsRetryableTornError(HttpStatusCode statusCode, int? tornCode, string? tornErrorMessage)
    {
        if (IsRetryableStatusCode(statusCode))
        {
            return true;
        }

        // Torn error codes are not publicly stable across all docs. We only treat obvious rate-limit signals as retryable.
        if (tornCode is 5)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(tornErrorMessage)
            && tornErrorMessage.Contains("too many", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string BuildUserSafeErrorMessage(HttpStatusCode statusCode, int? tornCode, string? tornError)
    {
        // Never include secrets. We also avoid echoing the request URI.
        var codePart = tornCode is null ? "" : $"Torn code {tornCode}. ";
        var errPart = string.IsNullOrWhiteSpace(tornError) ? "Unknown Torn error." : tornError;
        return $"Torn API error. {codePart}HTTP {(int)statusCode} ({statusCode}). {errPart}";
    }
}
