namespace HappyGymStats.Core.Reconstruction;

public static class SurfaceSeriesBuilder
{
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

    private static readonly (string Type, Func<GymLogEntry, double?> Before, Func<GymLogEntry, double?> Increased)[] KnownStatTypes =
    [
        ("strength", e => e.StrengthBefore, e => e.StrengthIncreased),
        ("defense", e => e.DefenseBefore, e => e.DefenseIncreased),
        ("speed", e => e.SpeedBefore, e => e.SpeedIncreased),
        ("dexterity", e => e.DexterityBefore, e => e.DexterityIncreased),
    ];

    public static SurfacePayload Build(
        IReadOnlyList<GymLogEntry> gymLogs,
        IReadOnlyDictionary<string, IReadOnlyList<ModifierProvenance>> provenanceByLogId)
    {
        var gymX = new List<double>();
        var gymY = new List<int>();
        var gymZ = new List<double>();
        var gymText = new List<string>();
        var gymConfidence = new List<double>();
        var gymConfidenceReasons = new List<string[]>();

        foreach (var row in gymLogs)
        {
            if (!TryReadGymPoint(row, out var statBefore, out var energyUsed, out var statIncreased, out var statType))
                continue;

            if (energyUsed <= 0)
                continue;

            provenanceByLogId.TryGetValue(row.LogId, out var provenanceRows);

            gymX.Add(statBefore);
            gymY.Add(row.HappyBeforeTrain ?? 0);
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
            EventX: Array.Empty<int>(),
            EventY: Array.Empty<int>(),
            EventZ: Array.Empty<int>(),
            EventText: Array.Empty<string>());
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
            var scopeStr = row.Scope;
            var statusStr = row.VerificationStatus;
            reasonSet.Add($"{scopeStr}-{statusStr}");

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

    private static bool TryReadGymPoint(GymLogEntry row, out double statBefore, out double energyUsed, out double statIncreased, out string statType)
    {
        statBefore = 0;
        energyUsed = 0;
        statIncreased = 0;
        statType = string.Empty;

        if (row.EnergyUsed is null)
            return false;

        energyUsed = row.EnergyUsed.Value;

        foreach (var (type, getBefore, getIncreased) in KnownStatTypes)
        {
            var before = getBefore(row);
            var increased = getIncreased(row);
            if (before != null && increased != null)
            {
                statType = type;
                statBefore = before.Value;
                statIncreased = increased.Value;
                return true;
            }
        }

        return false;
    }
}
