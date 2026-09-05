using System;
using System.IO;

namespace HappyGymStats.ArchitectureTests;

public sealed class SecurityRenderModeTests
{
    [Fact]
    public void Security_uses_the_server_router_and_defers_browser_crypto_until_after_render()
    {
        var security = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/Pages/Security.razor");
        var routes = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Routes.razor");

        Assert.Contains("@rendermode InteractiveServer", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", security, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject CryptoService", security, StringComparison.Ordinal);
        Assert.Contains("@inject IJSRuntime JS", security, StringComparison.Ordinal);
        Assert.Contains("private CryptoService Crypto => new(JS);", security, StringComparison.Ordinal);
        Assert.Contains("protected override async Task OnAfterRenderAsync(bool firstRender)", security, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_account_remains_server_rendered_and_does_not_depend_on_browser_crypto()
    {
        var playerAccount = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/PlayerAccount.razor");

        Assert.DoesNotContain("InteractiveWebAssembly", playerAccount, StringComparison.Ordinal);
        Assert.DoesNotContain("CryptoService", playerAccount, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HappyGymStats.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root from test output directory.");
        }

        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
