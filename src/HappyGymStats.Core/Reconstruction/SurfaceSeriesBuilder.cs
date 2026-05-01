using System.Text.Json;
namespace HappyGymStats.Reconstruction;

public static class SurfaceSeriesBuilder
{
    public sealed record RawGymLog(string LogId, DateTimeOffset OccurredAtUtc, string RawJson);
    public sealed record DerivedGym(string LogId, int HappyBeforeTrain);
    public sealed record DerivedHappyEvent(DateTimeOffset OccurredAtUtc, string EventType, int HappyBeforeEvent, int Delta, int HappyAfterEvent);

    public sealed record SurfacePayload(
        double[] GymX,
        int[] GymY,
        double[] GymZ,
        string[] GymText,
        int[] EventX,
        int[] EventY,
        int[] EventZ,
        string[] EventText);

    public static SurfacePayload Build(
        IReadOnlyList<RawGymLog> raws,
        IReadOnlyDictionary<string, DerivedGym> derivedByLogId,
        IReadOnlyList<DerivedHappyEvent> events)
    {
        var gymX = new List<double>();
        var gymY = new List<int>();
        var gymZ = new List<double>();
        var gymText = new List<string>();

        foreach (var row in raws)
        {
            if (!derivedByLogId.TryGetValue(row.LogId, out var derived))
                continue;

            if (!TryReadGymPoint(row.RawJson, out var statBefore, out var energyUsed, out var statIncreased, out var statType))
                continue;

            if (energyUsed <= 0)
                continue;

            gymX.Add(statBefore);
            gymY.Add(derived.HappyBeforeTrain);
            gymZ.Add(statIncreased / energyUsed);
            gymText.Add($"{statType} {row.OccurredAtUtc:O}");
        }

        return new SurfacePayload(
            GymX: gymX.ToArray(),
            GymY: gymY.ToArray(),
            GymZ: gymZ.ToArray(),
            GymText: gymText.ToArray(),
            EventX: events.Select(x => x.HappyBeforeEvent).ToArray(),
            EventY: events.Select(x => x.Delta).ToArray(),
            EventZ: events.Select(x => x.HappyAfterEvent).ToArray(),
            EventText: events.Select(x => $"{x.EventType} {x.OccurredAtUtc:O}").ToArray());
    }

    private static readonly (string Type, string Key)[] KnownStatTypes =
    {
        ("strength", "strength"),
        ("defense", "defense"),
        ("speed", "speed"),
        ("dexterity", "dexterity"),
    };

    private static bool TryReadGymPoint(string rawJson, out double statBefore, out double energyUsed, out double statIncreased, out string statType)
    {
        statBefore = 0;
        energyUsed = 0;
        statIncreased = 0;
        statType = string.Empty;

        using var doc = JsonDocument.Parse(rawJson);
        if (!TryGetPropertyIgnoreCase(doc.RootElement, "data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
            return false;

        if (!TryGetDouble(dataEl, "energy_used", out energyUsed))
            return false;

        foreach (var (type, key) in KnownStatTypes)
        {
            if (TryGetDouble(dataEl, $"{key}_before", out var before) && TryGetDouble(dataEl, $"{key}_increased", out var inc))
            {
                statType = type;
                statBefore = before;
                statIncreased = inc;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDouble(JsonElement obj, string name, out double value)
    {
        value = 0;
        if (!TryGetPropertyIgnoreCase(obj, name, out var el))
            return false;

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            if (obj.TryGetProperty(name, out value))
                return true;

            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
