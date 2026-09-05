using System.Net;
using System.Net.Http.Json;
using HappyGymStats.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

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
}
