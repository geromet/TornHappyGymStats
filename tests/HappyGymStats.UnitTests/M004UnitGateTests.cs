using System.Net;
using System.Text;
using ApiFailure = HappyGymStats.Blazor.Services.ApiFailure;
using SurfacesService = HappyGymStats.Blazor.Services.SurfacesService;

namespace HappyGymStats.Tests;

public sealed class M004FinalGateTests
{
    [Fact]
    public void MyStats_page_and_menu_are_explicitly_auth_marked_in_tracked_source()
    {
        var myStatsSource = ReadTrackedSource("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor");
        var layoutSource = ReadTrackedSource("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor");

        Assert.Contains("@attribute [Authorize]", myStatsSource, StringComparison.Ordinal);
        Assert.Contains("MudNavLink Href=\"/my-stats\"", layoutSource, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.Lock", layoutSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SurfacesService_uses_claim_bound_me_endpoints_in_tracked_source()
    {
        var source = ReadTrackedSource("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs");

        Assert.Contains("private const string MyStatsEndpoint = \"/api/v1/torn/surfaces/me\";", source, StringComparison.Ordinal);
        Assert.Contains("private const string MyStatsImportEndpoint = \"/api/v1/torn/import-jobs/me\";", source, StringComparison.Ordinal);
        Assert.Contains("http.GetAsync(MyStatsEndpoint", source, StringComparison.Ordinal);
        Assert.Contains("http.PostAsJsonAsync(MyStatsImportEndpoint", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MyStats_import_failures_do_not_echo_api_key_in_error_messages()
    {
        const string secret = "top-secret-api-key";
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartMyStatsImportAsync(secret));

        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, failure.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MyStats_import_malformed_json_is_classified_as_deserialization_failure()
    {
        using var http = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"outcome\":", Encoding.UTF8, "application/json")
            });
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartMyStatsImportAsync("safe-key"));

        Assert.Contains("/api/v1/torn/import-jobs/me", failure.Endpoint, StringComparison.Ordinal);
        Assert.Equal(HappyGymStats.Blazor.Services.ApiFailureCategory.Deserialization, failure.Category);
    }

    private static string ReadTrackedSource(string relativePath)
    {
        var root = ResolveRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");

        return dir.FullName;
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubMessageHandler(responder)) { BaseAddress = new Uri("http://localhost") };

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
