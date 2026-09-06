using System.Text.RegularExpressions;
using HappyGymStats.Core.Torn;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Regression coverage ensuring pagination continuations do not retain request authentication material.
/// </summary>
public sealed class PersistedTornCursorSecretTests
{
    [Theory]
    [InlineData(
        "https://api.torn.com/v2/user/log?key=SECRET123&limit=100",
        "https://api.torn.com/v2/user/log?limit=100")]
    [InlineData(
        "https://api.torn.com/v2/user/log?limit=100&key=SECRET123",
        "https://api.torn.com/v2/user/log?limit=100")]
    [InlineData(
        "https://api.torn.com/v2/user/log?KEY=SECRET123&limit=100",
        "https://api.torn.com/v2/user/log?limit=100")]
    [InlineData(
        "https://api.torn.com/v2/user/log?key=SECRET123",
        "https://api.torn.com/v2/user/log")]
    [InlineData(
        "/v2/faction/warfareranked?key=SECRET123&cat=ranked",
        "/v2/faction/warfareranked?cat=ranked")]
    [InlineData(
        "https://api.torn.com/v2/user/log?key=SECRET123&limit=100#page",
        "https://api.torn.com/v2/user/log?limit=100#page")]
    public void StripApiKeyFromUrl_removes_the_key_parameter(string input, string expected)
    {
        var actual = TornApiClient.StripApiKeyFromUrl(input);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("SECRET123", actual ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://api.torn.com/v2/user/log")]
    [InlineData("https://api.torn.com/v2/user/log?limit=100&keyed=1")]
    public void StripApiKeyFromUrl_leaves_key_free_urls_untouched(string? input)
    {
        Assert.Equal(input, TornApiClient.StripApiKeyFromUrl(input));
    }

    [Fact]
    public void No_persisted_cursor_is_assigned_a_raw_links_next_value()
    {
        var offenders = new List<string>();

        foreach (var relativePath in new[]
        {
            "src/HappyGymStats.Core/Torn/TornApiClient.cs",
            "src/HappyGymStats.WarPoller/RankedWarHistoryBackfillWorker.cs",
        })
        {
            var source = ReadTrackedSource(relativePath);
            foreach (Match match in Regex.Matches(source, @"^.*Links\?\.Next.*$", RegexOptions.Multiline))
            {
                if (!match.Value.Contains("StripApiKeyFromUrl", StringComparison.Ordinal))
                {
                    offenders.Add($"{relativePath}: {match.Value.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Torn pagination continuations must remove request authentication material before storage. Offending lines:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    private static string ReadTrackedSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
        }

        return File.ReadAllText(Path.Combine(dir.FullName, relativePath));
    }
}
