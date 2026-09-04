using System.Net;
using System.Text.RegularExpressions;
using HappyGymStats.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class InternalWarNotificationBoundaryTests : IClassFixture<SqliteApiEndpointTests.SqliteTestApplicationFactory>
{
    private const string InternalNotifyPath = "/api/v1/war/internal/notify";
    private readonly SqliteApiEndpointTests.SqliteTestApplicationFactory _factory;

    public InternalWarNotificationBoundaryTests(SqliteApiEndpointTests.SqliteTestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Proxied_request_cannot_invoke_internal_notify_action()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, InternalNotifyPath);
        request.Headers.TryAddWithoutValidation("X-Real-IP", "203.0.113.42");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.42");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void Direct_loopback_request_without_proxy_provenance_is_internal()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        Assert.True(InternalHttpRequestBoundary.IsDirectInternalTransport(context));
    }

    [Fact]
    public void Non_loopback_request_is_not_internal()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");

        Assert.False(InternalHttpRequestBoundary.IsDirectInternalTransport(context));
    }

    [Theory]
    [InlineData("Forwarded")]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Forwarded-Host")]
    [InlineData("X-Forwarded-Proto")]
    [InlineData("X-Real-IP")]
    public void Proxy_provenance_disqualifies_even_loopback_upstream(string header)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers[header] = "attacker.example";

        Assert.False(InternalHttpRequestBoundary.IsDirectInternalTransport(context));
    }

    [Theory]
    [InlineData("infra/nginx-torn.conf")]
    [InlineData("infra/nginx-torndev.conf")]
    public void Public_nginx_configs_explicitly_hide_internal_notify_route(string relativePath)
    {
        var config = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), relativePath));
        var exactLocation = Regex.Match(
            config,
            @"location\s*=\s*/api/v1/war/internal/notify\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(exactLocation.Success, $"{relativePath} must define an exact-match deny for {InternalNotifyPath}.");
        Assert.Contains("return 404;", exactLocation.Groups["body"].Value, StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
    }
}
