using System;
using System.IO;
using System.Linq;
using HappyGymStats.Identity.Authentication;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Asserts the Keycloak group→role mapping exists in exactly one place (#109).
/// </summary>
/// <remarks>
/// The API previously held its own copy of the group switch. It drifted from the
/// corrected version and threw for every user who was actually in a mapped group;
/// <c>RestrictedAccessExtensions</c> separately repeated the "/admins" literal,
/// held in step with a comment rather than the compiler. Neither divergence was
/// detectable by the type system, so it is checked here.
/// </remarks>
public sealed class KeycloakGroupMappingIsSingleSourceTests
{
    private static readonly string[] GroupLiterals =
        [KeycloakGroups.Admins, KeycloakGroups.FactionOwners, KeycloakGroups.Users];

    /// <summary>The one file allowed to spell the group names out.</summary>
    private const string DefiningFile = "KeycloakGroups.cs";

    [Fact]
    public void Only_KeycloakGroups_spells_out_the_group_names()
    {
        var sources = Directory
            .EnumerateFiles(RepoPath("src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        // A scan that finds no files to scan has proved nothing — the same vacuous
        // pass that let a privacy check report success while searching an empty
        // stream (#57).
        Assert.True(sources.Length > 50, $"expected to scan the whole src tree, found only {sources.Length} .cs files");

        var offenders = sources
            .Where(path => Path.GetFileName(path) != DefiningFile)
            .Select(path => (path, text: File.ReadAllText(path)))
            .Where(f => GroupLiterals.Any(g => f.text.Contains($"\"{g}\"", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(RepoPath("."), f.path))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Keycloak group names are spelled out outside {DefiningFile}; reference KeycloakGroups instead:\n"
            + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_group_constant_maps_to_a_role()
    {
        foreach (var group in GroupLiterals)
            Assert.False(string.IsNullOrEmpty(KeycloakGroups.RoleFor(group)), $"{group} maps to no role");
    }

    private static string RepoPath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not locate the repository root from the test output directory.");

        return Path.GetFullPath(Path.Combine([dir.FullName, .. segments]));
    }
}
