
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HappyGymStats.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using ApiFailure = HappyGymStats.Blazor.Services.ApiFailure;
using SurfacesService = HappyGymStats.Blazor.Services.SurfacesService;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class M004FinalGateTests : IClassFixture<SqliteApiEndpointTests.SqliteTestApplicationFactory>
{
    private readonly SqliteApiEndpointTests.SqliteTestApplicationFactory _factory;

    public M004FinalGateTests(SqliteApiEndpointTests.SqliteTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public void MyStats_page_and_menu_are_explicitly_auth_marked_in_tracked_source()
    {
        var myStatsSource = ReadTrackedSource("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor");
        var layoutSource = ReadTrackedSource("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor");

        Assert.Contains("@attribute [Authorize]", myStatsSource, StringComparison.Ordinal);

        var myStatsLink = layoutSource.IndexOf("MudNavLink Href=\"/my-stats\"", StringComparison.Ordinal);
        Assert.True(myStatsLink >= 0, "My Training must remain reachable through /my-stats navigation.");

        var authorizedStart = layoutSource.LastIndexOf("<Authorized>", myStatsLink, StringComparison.Ordinal);
        var authorizedEnd = layoutSource.IndexOf("</Authorized>", myStatsLink, StringComparison.Ordinal);
        Assert.True(
            authorizedStart >= 0 && authorizedEnd > myStatsLink,
            "The /my-stats navigation entry must remain inside an Authorized block.");
        Assert.Contains("My Training", layoutSource, StringComparison.Ordinal);
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
    public async Task Surfaces_me_rejects_invalid_claim_with_401()
    {
        using var client = _factory.CreateAuthenticatedClient("not-a-guid");

        var response = await client.GetAsync("/api/v1/torn/surfaces/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_me_requires_identity_map_and_subject_match()
    {
        var callerAnonymousId = Guid.NewGuid();

        using var missingMapClient = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString(), keycloakSub: "expected-sub");
        var missingMapResponse = await missingMapClient.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new { apiKey = "safe-key" });
        Assert.Equal(HttpStatusCode.Conflict, missingMapResponse.StatusCode);

        await _factory.SeedIdentityMapEntriesAsync(new IdentityMapEntity
        {
            AnonymousId = callerAnonymousId,
            KeycloakSub = "mapped-sub",
            IsProvisional = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        using var mismatchClient = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString(), keycloakSub: "different-sub");
        var mismatchResponse = await mismatchClient.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new { apiKey = "safe-key" });

        Assert.Equal(HttpStatusCode.Forbidden, mismatchResponse.StatusCode);
    }

    [Fact]
    public async Task Import_me_ignores_body_owner_tampering_and_binds_to_caller()
    {
        var callerAnonymousId = Guid.NewGuid();
        var attackerAnonymousId = Guid.NewGuid();

        await _factory.SeedIdentityMapEntriesAsync(new IdentityMapEntity
        {
            AnonymousId = callerAnonymousId,
            KeycloakSub = "test-sub",
            IsProvisional = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString(), keycloakSub: "test-sub");
        var response = await client.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new
        {
            apiKey = "safe-key",
            anonymousId = attackerAnonymousId,
            ownerAnonymousId = attackerAnonymousId,
            fresh = false,
        });

        Assert.True(response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<HappyGymStats.Core.Import.ImportOrchestrator>();
        Assert.NotNull(orchestrator.Latest);
        Assert.Equal(callerAnonymousId, orchestrator.Latest!.AnonymousId);
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
