using System.Globalization;
using System.Text.Json;

namespace HappyGymStats.Export;

internal static class GymModifierNormalizer
{
    internal sealed record GymModifiers(
        double Strength,
        double Defense,
        double Speed,
        double Dexterity
    );

    internal sealed class GymModifierTable
    {
        private readonly Dictionary<int, GymModifiers> _byId;

        private GymModifierTable(Dictionary<int, GymModifiers> byId)
        {
            _byId = byId;
        }

        public static bool TryLoadFromDefaultLocations(out GymModifierTable? table, out string? error)
        {
            table = null;
            error = null;

            // Search order: current working directory, then alongside the executable.
            var candidates = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "gyms.json"),
                Path.Combine(AppContext.BaseDirectory, "gyms.json"),
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    table = Load(path);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to load gyms.json at '{path}': {ex.Message}";
                    return false;
                }
            }

            // Not found is not an error — caller can decide whether to proceed.
            return true;
        }

        private static GymModifierTable Load(string path)
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("gyms", out var gymsEl) || gymsEl.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Expected root object with property 'gyms'.");

            var map = new Dictionary<int, GymModifiers>();

            foreach (var gymProp in gymsEl.EnumerateObject())
            {
                if (!int.TryParse(gymProp.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gymId))
                    continue;

                var g = gymProp.Value;

                double ReadFactor(string propName)
                {
                    if (!g.TryGetProperty(propName, out var p))
                        return 0;

                    if (p.ValueKind == JsonValueKind.Number)
                        return p.GetDouble() / 10.0;

                    if (p.ValueKind == JsonValueKind.String &&
                        double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                        return dv / 10.0;

                    return 0;
                }

                map[gymId] = new GymModifiers(
                    Strength: ReadFactor("strength"),
                    Defense: ReadFactor("defense"),
                    Speed: ReadFactor("speed"),
                    Dexterity: ReadFactor("dexterity")
                );
            }

            return new GymModifierTable(map);
        }

        public bool TryGetMultiplier(int gymId, string statKey, out double multiplier)
        {
            multiplier = 0;

            if (!_byId.TryGetValue(gymId, out var g))
                return false;

            multiplier = statKey switch
            {
                "strength" => g.Strength,
                "defense" => g.Defense,
                "speed" => g.Speed,
                "dexterity" => g.Dexterity,
                _ => 0
            };

            return multiplier > 0;
        }
    }

    public static void ApplyNormalization(Dictionary<string, string> flat, GymModifierTable? table)
    {
        if (table is null)
            return;

        if (!flat.TryGetValue("data.gym", out var gymText) ||
            !int.TryParse(gymText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gymId))
        {
            return;
        }

        NormalizeStat(flat, table, gymId, "strength");
        NormalizeStat(flat, table, gymId, "defense");
        NormalizeStat(flat, table, gymId, "speed");
        NormalizeStat(flat, table, gymId, "dexterity");
    }

    private static void NormalizeStat(Dictionary<string, string> flat, GymModifierTable table, int gymId, string stat)
    {
        var rawKey = $"data.{stat}_increased";
        if (!flat.TryGetValue(rawKey, out var rawText) || string.IsNullOrWhiteSpace(rawText))
            return;

        if (!double.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            return;

        if (!table.TryGetMultiplier(gymId, stat, out var mult) || mult <= 0)
            return;

        var normalized = raw / mult;
        var outKey = $"data.{stat}_increased_normalized";
        flat[outKey] = normalized.ToString("0.###############", CultureInfo.InvariantCulture);
    }
}
