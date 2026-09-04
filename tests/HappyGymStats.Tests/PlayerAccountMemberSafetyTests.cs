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
