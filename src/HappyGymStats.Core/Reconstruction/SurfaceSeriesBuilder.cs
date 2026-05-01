using System.Text.Json;

namespace HappyGymStats.Core.Reconstruction;

public static class SurfaceSeriesBuilder
{
    public sealed record RawGymLog(string LogId, DateTimeOffset OccurredAtUtc, string RawJson);
    public sealed record DerivedGym(string LogId, int? HappyBeforeTrain);
    public sealed record DerivedHappyEvent(DateTimeOffset OccurredAtUtc, string EventType, int HappyBeforeEvent, int Delta, int HappyAfterEvent);
    public sealed record ModifierProvenance(string Scope, string VerificationStatus, string VerificationReasonCode);

    public sealed record SurfacePayload(
        double[] GymX,
        int[] GymY,
        double[] GymZ,
        string[] GymText,
        double[] GymConfidence,
        string[][] GymConfidenceReasons,
        int[] EventX,
        int[] EventY,
        int[] EventZ,
        string[] EventText);

    public static SurfacePayload Build(
        IReadOnlyList<RawGymLog> raws,
        IReadOnlyDictionary<string, DerivedGym> derivedByLogId,
        IReadOnlyList<DerivedHappyEvent> events,
        IReadOnlyDictionary<string, IReadOnlyList<ModifierProvenance>> provenanceByLogId)
    {
        var gymX = new List<double>();
        var gymY = new List<int>();
        var gymZ = new List<double>();
        var gymText = new List<string>();
        var gymConfidence = new List<double>();
        var gymConfidenceReasons = new List<string[]>();

        foreach (var row in raws)
        {
            if (!TryReadGymPoint(row.RawJson, out var statBefore, out var energyUsed, out var statIncreased, out var statType))
                continue;

            if (energyUsed <= 0)
                continue;

            derivedByLogId.TryGetValue(row.LogId, out var derived);
            provenanceByLogId.TryGetValue(row.LogId, out var provenanceRows);

            gymX.Add(statBefore);
            gymY.Add(derived?.HappyBeforeTrain ?? 0);
            gymZ.Add(statIncreased / energyUsed);
            gymText.Add($"{statType} {row.OccurredAtUtc:O}");

            var confidence = ComputeConfidence(provenanceRows ?? Array.Empty<ModifierProvenance>(), out var reasons);
            gymConfidence.Add(confidence);
            gymConfidenceReasons.Add(reasons);
        }

        return new SurfacePayload(
            GymX: gymX.ToArray(),
            GymY: gymY.ToArray(),
            GymZ: gymZ.ToArray(),
            GymText: gymText.ToArray(),
            GymConfidence: gymConfidence.ToArray(),
            GymConfidenceReasons: gymConfidenceReasons.ToArray(),
            EventX: events.Select(x => x.HappyBeforeEvent).ToArray(),
            EventY: events.Select(x => x.Delta).ToArray(),
            EventZ: events.Select(x => x.HappyAfterEvent).ToArray(),
            EventText: events.Select(x => $"{x.EventType} {x.OccurredAtUtc:O}").ToArray());
    }

    private static double ComputeConfidence(IReadOnlyList<ModifierProvenance> rows, out string[] reasons)
    {
        if (rows.Count == 0)
        {
            reasons = ["missing-provenance-record"];
            return 0.2;
        }

        var score = 1.0;
        var reasonSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            reasonSet.Add(row.VerificationReasonCode);

            score *= row.VerificationStatus switch
            {
                "verified" => 1.0,
                "unresolved" => 0.75,
                "unavailable" => 0.6,
                _ => 0.5
            };
        }

        reasons = reasonSet.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return Math.Round(Math.Clamp(score, 0.0, 1.0), 4);
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
