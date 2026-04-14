using System.Net;
using System.Text;

using HappyGymStats.Torn;

namespace HappyGymStats.Tests.Torn;

public sealed class TornApiClientTests
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

    [Fact]
    public async Task GetUserLogPageAsync_SendsApiKeyOnlyInAuthorizationHeader()
    {
        const string apiKey = "sk_test_super_secret";
        var url = new Uri("https://api.torn.com/v2/user/log?from=123");

        var handler = new FakeHandler((request, _) =>
        {
            Assert.NotNull(request.RequestUri);
            Assert.DoesNotContain(apiKey, request.RequestUri!.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("apikey", request.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);

            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("ApiKey", request.Headers.Authorization!.Scheme);
            Assert.Equal(apiKey, request.Headers.Authorization!.Parameter);

            var json = "{\"log\":[],\"_metadata\":{\"links\":{}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var httpClient = new HttpClient(handler);
        var client = new TornApiClient(httpClient);

        var page = await client.GetUserLogPageAsync(apiKey, url, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Empty(page.Logs);
        Assert.Null(page.NextUrl);
    }

    [Fact]
    public async Task GetUserLogPageAsync_ParsesNextLink_AndPreservesOriginalString()
    {
        const string apiKey = "sk_test_super_secret";
        var url = new Uri("https://api.torn.com/v2/user/log?from=123");
        var next = "https://api.torn.com/v2/user/log?from=999&sort=asc";

        var handler = new FakeHandler((_, _) =>
        {
            var json = $"{{\"log\":[],\"_metadata\":{{\"links\":{{\"next\":\"{next}\"}}}}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));
        var page = await client.GetUserLogPageAsync(apiKey, url, CancellationToken.None);

        Assert.NotNull(page.NextUrl);
        Assert.Equal(next, page.NextUrl!.OriginalString);
    }

    [Fact]
    public async Task GetUserLogPageAsync_WhenTornErrorPayload_ReturnsHardStopUserSafeMessage()
    {
        const string apiKey = "sk_test_super_secret";
        var url = new Uri("https://api.torn.com/v2/user/log?from=123");

        var handler = new FakeHandler((_, _) =>
        {
            var json = "{\"code\":2,\"error\":\"Incorrect key\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TornApiException>(() => client.GetUserLogPageAsync(apiKey, url, CancellationToken.None));

        Assert.False(ex.IsRetryable);
        Assert.Contains("Incorrect key", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(apiKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUserLogPageAsync_WhenApiKeyMissing_ThrowsArgumentException()
    {
        var client = new TornApiClient(new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetUserLogPageAsync(" ", new Uri("https://api.torn.com/v2/user/log"), CancellationToken.None));
    }

    [Fact]
    public async Task GetUserLogPageAsync_WhenUrlIsRelative_ThrowsArgumentException()
    {
        var client = new TornApiClient(new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetUserLogPageAsync("secret", new Uri("/v2/user/log", UriKind.Relative), CancellationToken.None));
    }

    [Fact]
    public async Task GetUserLogPageAsync_WhenMetadataMissing_IsRetryableMalformedResponse()
    {
        var handler = new FakeHandler((_, _) =>
        {
            var json = "{\"log\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TornApiException>(() =>
            client.GetUserLogPageAsync("secret", new Uri("https://api.torn.com/v2/user/log"), CancellationToken.None));

        Assert.True(ex.IsRetryable);
        Assert.Contains("_metadata", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUserLogPageAsync_WhenNextLinkNotAbsolute_IsRetryableMalformedResponse()
    {
        var handler = new FakeHandler((_, _) =>
        {
            var json = "{\"log\":[],\"_metadata\":{\"links\":{\"next\":\"/relative\"}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TornApiException>(() =>
            client.GetUserLogPageAsync("secret", new Uri("https://api.torn.com/v2/user/log"), CancellationToken.None));

        Assert.True(ex.IsRetryable);
        Assert.Contains("absolute", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUserLogPageAsync_WhenHttp429_IsRetryable()
    {
        var handler = new FakeHandler((_, _) =>
        {
            var json = "{\"code\":5,\"error\":\"Too many requests\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        var client = new TornApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TornApiException>(() =>
            client.GetUserLogPageAsync("secret", new Uri("https://api.torn.com/v2/user/log"), CancellationToken.None));

        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task GetUserLogPageAsync_WhenNetworkException_IsRetryable()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("boom"));
        var client = new TornApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TornApiException>(() =>
            client.GetUserLogPageAsync("secret", new Uri("https://api.torn.com/v2/user/log"), CancellationToken.None));

        Assert.True(ex.IsRetryable);
    }
}
