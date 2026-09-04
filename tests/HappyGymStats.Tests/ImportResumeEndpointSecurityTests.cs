using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ImportResumeEndpointSecurityTests
{
    [Fact]
    [Trait("Category", "SqliteApiEndpoint")]
    public async Task Public_import_endpoint_rejects_ownerless_resume()
    {
        using var factory = new SqliteApiEndpointTests.SqliteTestApplicationFactory();
        factory.ResetDatabase();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/torn/import-jobs",
            new { apiKey = "test-key", fresh = false });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
