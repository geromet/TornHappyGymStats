using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarFinalAssemblyGuardrailTests
{
    [Fact]
    public void Api_program_registers_signalr_and_war_hub_route()
    {
        var content = ReadRepoFile("src/HappyGymStats.Api/Program.cs");

        Assert.Contains("builder.Services.AddSignalR();", content, StringComparison.Ordinal);
        Assert.Contains("app.MapHub<WarHub>(\"/api/hub/war\");", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Nginx_routes_war_hub_before_general_api_and_forwards_websocket_headers()
    {
        var content = ReadRepoFile("infra/nginx-torn.conf");

        var hubIndex = content.IndexOf("location /api/hub/war {", StringComparison.Ordinal);
        var apiIndex = content.IndexOf("location /api/ {", StringComparison.Ordinal);

        Assert.True(hubIndex >= 0, "Expected dedicated /api/hub/war nginx location.");
        Assert.True(apiIndex >= 0, "Expected general /api/ nginx location.");
        Assert.True(hubIndex < apiIndex, "Expected /api/hub/war location to appear before the general /api/ location.");
        Assert.Contains("proxy_set_header   Upgrade           $http_upgrade;", content, StringComparison.Ordinal);
        Assert.Contains("proxy_set_header   Connection        \"upgrade\";", content, StringComparison.Ordinal);
        Assert.Contains("proxy_read_timeout 300s;", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Env_example_documents_loopback_hub_notify_settings()
    {
        var content = ReadRepoFile("infra/.env.example");

        Assert.Contains("WarPoller__HubNotifyUrl=", content, StringComparison.Ordinal);
        Assert.Contains("/api/v1/war/internal/notify", content, StringComparison.Ordinal);
        Assert.Contains("WarPoller__HubNotifyTimeoutSeconds=5", content, StringComparison.Ordinal);
    }

    [Fact]
    public void War_api_hub_and_board_boundary_files_avoid_forbidden_direct_torn_or_personal_lane_dependencies()
    {
        var files = new[]
        {
            "src/HappyGymStats.Api/Controllers/WarController.cs",
            "src/HappyGymStats.Api/Hubs/WarHub.cs",
            "src/HappyGymStats.Api/Hubs/WarHubBroadcaster.cs",
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/WarDtos.cs",
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/WarBoardService.cs",
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor"
        };

        var forbiddenTokens = new[]
        {
            "TornApiClient",
            "api.torn.com",
            "Centrifugo",
            "centrifugo",
            "ajax",
            "scrap",
            "PersonalLane",
            "personal-lane"
        };

        var contents = files.ToDictionary(path => path, ReadRepoFile);

        var violations = contents
            .SelectMany(entry => forbiddenTokens
                .Where(token => entry.Value.Contains(token, StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{entry.Key} -> {token}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Forbidden direct Torn/ajax/Centrifugo/scraping/personal-lane references found:\n" + string.Join(Environment.NewLine, violations));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HappyGymStats.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
