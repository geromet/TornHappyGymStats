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
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.UseSetting(DevelopmentAuthenticationExtensions.EnabledKey, "1");
                    builder.UseSetting("HAPPYGYMSTATS_DEV_AUTH_SQLITE_PATH", sqlitePath);
                    builder.UseSetting("HAPPYGYMSTATS_DEV_SKIP_WAR_SEED", "1");
                });
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(DevelopmentAuthenticationExtensions.UserHeaderName, "objective-caller");
            if (elevatedRole is not null)
            {
                client.DefaultRequestHeaders.Add(
                    DevelopmentAuthenticationExtensions.RoleHeaderName,
                    elevatedRole);
            }

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
}
