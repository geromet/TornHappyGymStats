using System.Text.Json;

namespace HappyGymStats.Core.Reconstruction;

public static class ModifierOverrideLoader
{
    public const int MaxEntries = 500;
    public const int MaxFieldLength = 128;

    public static ModifierOverrideLoadResult LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ModifierOverrideLoadResult(Array.Empty<ModifierOverrideEntry>(), new[] { "override-read-failed" }, 0, 0, false);
        }

        try
        {
            var json = File.ReadAllText(path);
            return LoadFromJson(json);
        }
        catch
        {
            return new ModifierOverrideLoadResult(Array.Empty<ModifierOverrideEntry>(), new[] { "override-read-failed" }, 0, 0, false);
        }
    }

    public static ModifierOverrideLoadResult LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ModifierOverrideLoadResult(Array.Empty<ModifierOverrideEntry>(), new[] { "override-parse-failed" }, 0, 0, false);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("overrides", out var overridesElement) || overridesElement.ValueKind != JsonValueKind.Array)
                return new ModifierOverrideLoadResult(Array.Empty<ModifierOverrideEntry>(), new[] { "override-parse-failed" }, 0, 0, false);

            var map = new Dictionary<(string Scope, string Placeholder), ModifierOverrideEntry>();
            var skipped = 0;
            var capped = false;

            foreach (var item in overridesElement.EnumerateArray())
            {
                if (map.Count >= MaxEntries)
                {
                    capped = true;
                    break;
                }

                if (!TryParseEntry(item, out var entry))
                {
                    skipped++;
                    continue;
                }

                map[(entry.Scope, entry.PlaceholderId)] = entry; // last-write-wins
            }

            return new ModifierOverrideLoadResult(map.Values.OrderBy(x => x.Scope, StringComparer.Ordinal).ThenBy(x => x.PlaceholderId, StringComparer.Ordinal).ToArray(), Array.Empty<string>(), skipped, map.Count, capped);
        }
        catch (JsonException)
        {
            return new ModifierOverrideLoadResult(Array.Empty<ModifierOverrideEntry>(), new[] { "override-parse-failed" }, 0, 0, false);
        }
    }

    private static bool TryParseEntry(JsonElement item, out ModifierOverrideEntry entry)
    {
        entry = default!;

        if (!TryGetBounded(item, "scope", out var scope) || !TryGetBounded(item, "placeholderId", out var placeholderId))
            return false;

        if (!string.Equals(scope, HappyReconstructionModels.ModifierProvenanceScopes.Faction, StringComparison.Ordinal)
            && !string.Equals(scope, HappyReconstructionModels.ModifierProvenanceScopes.Company, StringComparison.Ordinal))
            return false;

        if (!TryGetBounded(item, "resolvedId", out var resolvedId) || !TryGetBounded(item, "linkTarget", out var linkTarget))
            return false;

        entry = new ModifierOverrideEntry(scope, placeholderId, resolvedId, linkTarget);
        return true;
    }

    private static bool TryGetBounded(JsonElement item, string key, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        var s = element.GetString();
        if (string.IsNullOrWhiteSpace(s) || s.Length > MaxFieldLength)
            return false;

        value = s;
        return true;
    }
}

public sealed record ModifierOverrideEntry(string Scope, string PlaceholderId, string ResolvedId, string LinkTarget);

public sealed record ModifierOverrideLoadResult(
    IReadOnlyList<ModifierOverrideEntry> Entries,
    IReadOnlyList<string> Diagnostics,
    int SkippedMalformedEntryCount,
    int LoadedEntryCount,
    bool HitEntryCap)
{
    public ModifierOverrideEntry? Find(string scope, string? placeholderId)
    {
        if (string.IsNullOrWhiteSpace(placeholderId))
            return null;

        return Entries.FirstOrDefault(x => string.Equals(x.Scope, scope, StringComparison.Ordinal)
            && string.Equals(x.PlaceholderId, placeholderId, StringComparison.Ordinal));
    }
}
