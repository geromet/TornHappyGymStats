using System;
using System.IO;

namespace HappyGymStats.ArchitectureTests;

public sealed class SecurityRenderModeTests
{
    [Fact]
    public void Security_disables_server_prerender_for_browser_only_crypto_service()
    {
        var security = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/Pages/Security.razor");

        Assert.Contains(
            "@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))",
            security,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@rendermode InteractiveWebAssembly\n",
            security,
            StringComparison.Ordinal);
        Assert.Contains("@inject CryptoService Crypto", security, StringComparison.Ordinal);
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
