using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class PlayerAccountMemberSafetyTests
{
    [Fact]
    public void Player_account_keeps_technical_identity_diagnostics_off_member_surface()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/PlayerAccount.razor");

        Assert.Contains("Confirm which Happy Gym Stats account is currently signed in.", content, StringComparison.Ordinal);
        Assert.Contains("Technical sign-in diagnostics are intentionally kept out of member-facing pages.", content, StringComparison.Ordinal);
        Assert.Contains("<td>Signed in</td>", content, StringComparison.Ordinal);

        Assert.DoesNotContain("Claim diagnostics", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Subject (sub)", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Issuer", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Total claims", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Roles & groups", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Keycloak", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@claim.Type", content, StringComparison.Ordinal);
        Assert.DoesNotContain("@claim.Value", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_account_is_the_member_connection_management_surface()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/PlayerAccount.razor");

        Assert.Contains("@rendermode InteractiveServer", content, StringComparison.Ordinal);
        Assert.Contains("Torn connection", content, StringComparison.Ordinal);
        Assert.Contains("Connect Torn", content, StringComparison.Ordinal);
        Assert.Contains("Replace Torn API key", content, StringComparison.Ordinal);
        Assert.Contains("Confirm revoke", content, StringComparison.Ordinal);
        Assert.Contains("Consent &amp; privacy", content, StringComparison.Ordinal);
        Assert.Contains("InputType.Password", content, StringComparison.Ordinal);
        Assert.Contains("_tornApiKey = null", content, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_does_not_expose_placeholder_developer_controls()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Settings.razor");

        Assert.DoesNotContain("API base URL", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Use compact cards", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Save settings", content, StringComparison.Ordinal);
        Assert.Contains("/player-account", content, StringComparison.Ordinal);
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
