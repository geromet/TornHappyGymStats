using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class LoginMemberSafetyTests
{
    [Fact]
    public void Login_keeps_infrastructure_and_raw_auth_diagnostics_off_member_surface()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Login.razor");

        Assert.Contains("Sign in to Happy Gym Stats.", content, StringComparison.Ordinal);
        Assert.Contains("Sign-in failed. Please try again.", content, StringComparison.Ordinal);
        Assert.Contains("LocalRedirectPolicy.Normalize(ReturnUrl)", content, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString(_resolvedReturnUrl)", content, StringComparison.Ordinal);

        Assert.DoesNotContain("Authenticate with Keycloak", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authenticated via Keycloak", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Client: happygymstats-web", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Return destination:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("preferred_username", content, StringComparison.Ordinal);
        Assert.DoesNotContain("roles count", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@AuthError", content, StringComparison.Ordinal);
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
