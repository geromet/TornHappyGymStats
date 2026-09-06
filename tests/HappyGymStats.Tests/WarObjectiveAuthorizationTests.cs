using System.Net;
using System.Net.Http.Json;
using HappyGymStats.Api;
using HappyGymStats.Core.War;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HappyGymStats.Tests;

public sealed class WarObjectiveAuthorizationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(Roles.FactionOwner)]
    public async Task Non_admin_cannot_append_objective_even_with_crafted_faction(string? elevatedRole)
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"hgs-war-objective-auth-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var factory = CreateFactory(sqlitePath);
            using var client = CreateAuthenticatedClient(factory, "objective-caller", elevatedRole);

            var response = await client.PostAsJsonAsync(
                "/api/v1/war/objectives",
                new
                {
                    factionId = 999999L,
                    warId = 12345L,
                    mode = WarObjectiveMode.TermedWin,
                    stopAtFactionScore = 1000,
                    notes = "crafted write"
                });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            if (File.Exists(sqlitePath))
                File.Delete(sqlitePath);
        }
    }

    [Theory]
    [InlineData(null, "/api/v1/war/objectives/999999/12345/current")]
    [InlineData(null, "/api/v1/war/objectives/999999/12345/evaluation?factionScore=10")]
    [InlineData(null, "/api/v1/war/objectives/999999/12345/history")]
    [InlineData(Roles.FactionOwner, "/api/v1/war/objectives/999999/12345/current")]
    [InlineData(Roles.FactionOwner, "/api/v1/war/objectives/999999/12345/evaluation?factionScore=10")]
    [InlineData(Roles.FactionOwner, "/api/v1/war/objectives/999999/12345/history")]
    public async Task Restricted_objective_reads_fail_closed_without_authoritative_faction_scope(
        string? elevatedRole,
        string route)
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"hgs-war-objective-read-auth-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var factory = CreateFactory(sqlitePath);
            using var client = CreateAuthenticatedClient(factory, "objective-reader", elevatedRole);

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("changedBy", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("notes", payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(sqlitePath))
                File.Delete(sqlitePath);
        }
    }

    [Theory]
    [InlineData("/api/v1/war/objectives/999999/12345/current")]
    [InlineData("/api/v1/war/objectives/999999/12345/evaluation?factionScore=10")]
    [InlineData("/api/v1/war/objectives/999999/12345/history")]
    public async Task Admin_retains_restricted_objective_read_access(string route)
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"hgs-war-objective-admin-read-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var factory = CreateFactory(sqlitePath);
            using var client = CreateAuthenticatedClient(factory, "objective-admin", Roles.Admin);

            var response = await client.GetAsync(route);

            response.EnsureSuccessStatusCode();
        }
        finally
        {
            if (File.Exists(sqlitePath))
                File.Delete(sqlitePath);
        }
    }

    [Fact]
    public void Append_request_has_no_client_controlled_creator_field()
    {
        var propertyNames = typeof(HappyGymStats.Api.Controllers.AppendWarObjectiveRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("creator", StringComparison.OrdinalIgnoreCase)
            || name.Contains("changedby", StringComparison.OrdinalIgnoreCase)
            || name.Contains("actor", StringComparison.OrdinalIgnoreCase));
    }

    private static WebApplicationFactory<Program> CreateFactory(string sqlitePath)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(DevelopmentAuthenticationExtensions.EnabledKey, "1");
                builder.UseSetting("HAPPYGYMSTATS_DEV_AUTH_SQLITE_PATH", sqlitePath);
                builder.UseSetting("HAPPYGYMSTATS_DEV_SKIP_WAR_SEED", "1");
            });

    private static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> factory,
        string user,
        string? role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationExtensions.UserHeaderName, user);
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(
                DevelopmentAuthenticationExtensions.RoleHeaderName,
                role);
        }

        return client;
    }
}
