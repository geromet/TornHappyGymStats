using HappyGymStats.Core.Reconstruction;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ModifierOverrideLoaderTests
{
    [Fact]
    public void LoadFromJson_accepts_valid_entries_and_uses_last_write_wins_for_duplicates()
    {
        const string json = """
        {
          "overrides": [
            { "scope": "faction", "placeholderId": "unknown-faction", "resolvedId": "111", "linkTarget": "/factions/111" },
            { "scope": "faction", "placeholderId": "unknown-faction", "resolvedId": "222", "linkTarget": "/factions/222" }
          ]
        }
        """;

        var result = ModifierOverrideLoader.LoadFromJson(json);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.LoadedEntryCount);
        Assert.Equal(0, result.SkippedMalformedEntryCount);
        Assert.False(result.HitEntryCap);

        var entry = result.Find("faction", "unknown-faction");
        Assert.NotNull(entry);
        Assert.Equal("222", entry!.ResolvedId);
        Assert.Equal("/factions/222", entry.LinkTarget);
    }

    [Fact]
    public void LoadFromJson_rejects_malformed_entries_and_unknown_scope_but_keeps_valid_subset()
    {
        const string json = """
        {
          "overrides": [
            { "scope": "faction", "placeholderId": "unknown-faction", "resolvedId": "111", "linkTarget": "/factions/111" },
            { "scope": "guild", "placeholderId": "unknown-guild", "resolvedId": "x", "linkTarget": "/guilds/x" },
            { "scope": "company", "placeholderId": "unknown-company", "resolvedId": "", "linkTarget": "/companies/123" }
          ]
        }
        """;

        var result = ModifierOverrideLoader.LoadFromJson(json);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.LoadedEntryCount);
        Assert.Equal(2, result.SkippedMalformedEntryCount);
        Assert.NotNull(result.Find("faction", "unknown-faction"));
        Assert.Null(result.Find("company", "unknown-company"));
    }

    [Fact]
    public void LoadFromJson_bad_json_returns_parse_failure_and_empty_set()
    {
        var result = ModifierOverrideLoader.LoadFromJson("{not-json");

        Assert.Contains("override-parse-failed", result.Diagnostics);
        Assert.Equal(0, result.LoadedEntryCount);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void LoadFromFile_missing_file_degrades_gracefully()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "modifier-overrides.local.json");

        var result = ModifierOverrideLoader.LoadFromFile(missingPath);

        Assert.Contains("override-read-failed", result.Diagnostics);
        Assert.Equal(0, result.LoadedEntryCount);
        Assert.Empty(result.Entries);
    }
}
