using System.Net;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class DevelopmentAuthenticationTests
{
    [Fact]
    public void ValidateCanEnable_throws_in_production()
    {
        var environment = new TestWebHostEnvironment { EnvironmentName = Environments.Production };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DevelopmentAuthenticationExtensions.ValidateCanEnable(environment));

        Assert.Contains(DevelopmentAuthenticationExtensions.EnabledKey, error.Message, StringComparison.Ordinal);
        Assert.Contains("Production", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_header_authenticates_default_user_and_required_role()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/secure");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("dev-war-planner", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Development_header_allows_user_override_for_browser_tests()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Add(DevelopmentAuthenticationExtensions.UserHeaderName, "uat-planner");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("uat-planner", await response.Content.ReadAsStringAsync());
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseEnvironment(Environments.Development);
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDevelopmentHeaderAuthentication();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/secure", async context =>
                        {
                            await context.Response.WriteAsync(context.User.Identity?.Name ?? string.Empty);
                        }).RequireAuthorization(policy => policy.RequireRole(Roles.User));
                    });
                });
            });

        var host = await builder.StartAsync();
        return host;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(DevelopmentAuthenticationTests);
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
