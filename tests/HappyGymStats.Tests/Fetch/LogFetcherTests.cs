using System.Net;
using System.Text;
using System.Text.Json;

using HappyGymStats.Fetch;
using HappyGymStats.Storage;
using HappyGymStats.Storage.Models;
using HappyGymStats.Torn;

namespace HappyGymStats.Tests.Fetch;

public sealed class LogFetcherTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    private static AppPaths TempPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", "fetch", Guid.NewGuid().ToString("N"));
        var quarantine = Path.Combine(dir, "quarantine");
        return new AppPaths(
            DataDirectory: dir,
            QuarantineDirectory: quarantine,
            CheckpointPath: Path.Combine(dir, "checkpoint.json"),
            LogsJsonlPath: Path.Combine(dir, "userlogs.jsonl"));
    }

    private static string BuildPageJson(IEnumerable<string> ids, string? nextUrl)
    {
        var logsJson = string.Join(",", ids.Select(id =>
            $"{{\"id\":\"{id}\",\"timestamp\":{1000 + Math.Abs(id.GetHashCode() % 10000)},\"details\":{{\"title\":\"t{id}\",\"category\":\"gym\"}}}}"));

        var nextPart = nextUrl is null
            ? "{}"
            : $"{{\"next\":\"{nextUrl}\"}}";

        return $"{{\"log\":[{logsJson}],\"_metadata\":{{\"links\":{nextPart}}}}}";
    }

    private static IReadOnlyList<string> ReadIds(string jsonlPath)
    {
        if (!File.Exists(jsonlPath))
            return Array.Empty<string>();

        var ids = new List<string>();
        foreach (var line in File.ReadAllLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("id", out var idEl));
            ids.Add(idEl.ValueKind == JsonValueKind.String ? idEl.GetString()! : idEl.GetInt64().ToString());
        }

        return ids;
    }

    [Fact]
    public async Task RunAsync_Fresh_DedupesAcrossPages_AndWritesTerminalCheckpoint()
    {
        const string apiKey = "sk_test";
        var paths = TempPaths();

        var url1 = new Uri("https://api.torn.com/v2/user/log?page=1");
        var url2 = new Uri("https://api.torn.com/v2/user/log?page=2");

        var handler = new FakeHandler((req, _) =>
        {
            Assert.NotNull(req.RequestUri);

            var json = req.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal)
                ? BuildPageJson(new string[] { "id1", "id2", "id3" }, url2.OriginalString)
                : BuildPageJson(new string[] { "id3", "id4" }, null);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));
        var fetcher = new LogFetcher(paths, client);

        var options = FetchOptions.Default(url1, TimeSpan.Zero) with { MaxRetryAttempts = 0 };
        var result = await fetcher.RunAsync(apiKey, FetchMode.Fresh, options, CancellationToken.None);

        var ids = ReadIds(paths.LogsJsonlPath);
        Assert.Equal(new string[] { "id1", "id2", "id3", "id4" }, ids.OrderBy(x => x, StringComparer.Ordinal));

        var checkpoint = CheckpointStore.TryRead(paths.CheckpointPath);
        Assert.NotNull(checkpoint);
        Assert.True(string.IsNullOrWhiteSpace(checkpoint!.NextUrl));
        Assert.Equal("completed", checkpoint.LastRunOutcome);

        Assert.Equal(2, result.PagesFetched);
        Assert.Equal(5, result.LogsFetched);
        Assert.Equal(4, result.LogsAppended);
    }

    [Fact]
    public async Task RunAsync_CancelAfterFirstPage_WritesCheckpoint_AndResumeContinuesWithoutDuplicates()
    {
        const string apiKey = "sk_test";
        var paths = TempPaths();

        var url1 = new Uri("https://api.torn.com/v2/user/log?page=1");
        var url2 = new Uri("https://api.torn.com/v2/user/log?page=2");

        var requestCount = 0;
        using var cancelCts = new CancellationTokenSource();

        var handler = new FakeHandler((req, _) =>
        {
            requestCount++;

            Assert.NotNull(req.RequestUri);
            var isFirst = req.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal);

            var json = isFirst
                ? BuildPageJson(new string[] { "id1", "id2", "id3" }, url2.OriginalString)
                : BuildPageJson(new string[] { "id3", "id4" }, null);

            if (isFirst)
            {
                // Cancel shortly after the first response completes parsing; fetcher should stop before starting the next request.
                Task.Run(async () =>
                {
                    await Task.Delay(10);
                    cancelCts.Cancel();
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));
        var fetcher = new LogFetcher(paths, client);
        var options = FetchOptions.Default(url1, TimeSpan.FromMinutes(1)); // delay should be interrupted by cancellation

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fetcher.RunAsync(apiKey, FetchMode.Fresh, options, cancelCts.Token));

        var checkpointAfterCancel = CheckpointStore.TryRead(paths.CheckpointPath);
        Assert.NotNull(checkpointAfterCancel);
        Assert.Equal(url2.OriginalString, checkpointAfterCancel!.NextUrl);
        Assert.Equal("cancelled", checkpointAfterCancel.LastRunOutcome);

        // Resume and ensure we only append the missing id=4.
        var resumeOptions = FetchOptions.Default(url1, TimeSpan.Zero) with { MaxRetryAttempts = 0 };
        var resumed = await fetcher.RunAsync(apiKey, FetchMode.Resume, resumeOptions, CancellationToken.None);

        var ids = ReadIds(paths.LogsJsonlPath);
        Assert.Equal(new string[] { "id1", "id2", "id3", "id4" }, ids.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal("completed", resumed.Checkpoint.LastRunOutcome);

        Assert.Equal(2, requestCount); // one for first run, one for resume
    }

    [Fact]
    public async Task RunAsync_WhenHttp429ThenSuccess_RetriesAndCompletes()
    {
        const string apiKey = "sk_test";
        var paths = TempPaths();

        var start = new Uri("https://api.torn.com/v2/user/log?page=1");
        var callCount = 0;

        var handler = new FakeHandler((_, _) =>
        {
            callCount++;

            if (callCount == 1)
            {
                var json = "{\"code\":5,\"error\":\"Too many requests\"}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }

            var okJson = BuildPageJson(new string[] { "id10" }, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(okJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));
        var fetcher = new LogFetcher(paths, client);

        var options = FetchOptions.Default(start, TimeSpan.Zero) with
        {
            MaxRetryAttempts = 2,
            InitialBackoffDelay = TimeSpan.FromMilliseconds(1),
            MaxBackoffDelay = TimeSpan.FromMilliseconds(5),
        };

        var result = await fetcher.RunAsync(apiKey, FetchMode.Fresh, options, CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal(1, result.LogsAppended);
        Assert.Equal("completed", result.Checkpoint.LastRunOutcome);
    }

    [Fact]
    public async Task RunAsync_WhenPersistent5xx_StopsAfterRetryBudget_AndRecordsFailure()
    {
        const string apiKey = "sk_test";
        var paths = TempPaths();

        var start = new Uri("https://api.torn.com/v2/user/log?page=1");
        var callCount = 0;

        var handler = new FakeHandler((_, _) =>
        {
            callCount++;
            var json = "{\"log\":[],\"_metadata\":{\"links\":{}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));
        var fetcher = new LogFetcher(paths, client);

        var options = FetchOptions.Default(start, TimeSpan.Zero) with
        {
            MaxRetryAttempts = 2,
            InitialBackoffDelay = TimeSpan.FromMilliseconds(1),
            MaxBackoffDelay = TimeSpan.FromMilliseconds(5),
        };

        await Assert.ThrowsAsync<TornApiException>(() =>
            fetcher.RunAsync(apiKey, FetchMode.Fresh, options, CancellationToken.None));

        Assert.Equal(3, callCount); // initial + 2 retries

        var checkpoint = CheckpointStore.TryRead(paths.CheckpointPath);
        Assert.NotNull(checkpoint);
        Assert.Equal("failed", checkpoint!.LastRunOutcome);
        Assert.False(string.IsNullOrWhiteSpace(checkpoint.LastErrorMessage));
    }

    [Fact]
    public async Task RunAsync_WhenCheckpointNextUrlInvalid_FailsFast_AndPersistsError()
    {
        const string apiKey = "sk_test";
        var paths = TempPaths();

        var bad = new Checkpoint(
            NextUrl: "not-a-url",
            LastLogId: null,
            LastLogTimestamp: null,
            LastLogTitle: null,
            LastLogCategory: null,
            TotalFetchedCount: 0,
            TotalAppendedCount: 0,
            LastRunStartedAt: null,
            LastRunCompletedAt: null,
            LastRunOutcome: null,
            LastErrorMessage: null,
            LastErrorAt: null);
        CheckpointStore.Write(paths.CheckpointPath, bad);

        var handler = new FakeHandler((_, _) => throw new Exception("should not be called"));
        var fetcher = new LogFetcher(paths, new TornApiClient(new HttpClient(handler)));

        var options = FetchOptions.Default(new Uri("https://api.torn.com/v2/user/log"), TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fetcher.RunAsync(apiKey, FetchMode.Resume, options, CancellationToken.None));

        var checkpoint = CheckpointStore.TryRead(paths.CheckpointPath);
        Assert.NotNull(checkpoint);
        Assert.Equal("failed", checkpoint!.LastRunOutcome);
        Assert.False(string.IsNullOrWhiteSpace(checkpoint.LastErrorMessage));
    }
}
