using System.Text.Json;
using HappyGymStats.Core.Reconstruction;
using static HappyGymStats.Core.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Legacy.Cli.Export;

/// <summary>
///     Reads the derived gym-train sidecar JSONL into a dictionary keyed by <c>log_id</c>.
///     Uses the same <c>SnakeCaseLower</c> JSON options as <see cref="DerivedGymTrainStore" />
///     so the serialized property names match.
/// </summary>
public static class DerivedGymTrainReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <summary>
    ///     Load derived gym-train records from a JSONL file, keyed by <c>log_id</c>.
    ///     If the file does not exist, returns an empty dictionary with <see cref="ReadResult.FileMissing" /> = true.
    ///     Malformed lines are skipped and counted.
    /// </summary>
    public static ReadResult Read(string derivedJsonlPath)
    {
        if (!File.Exists(derivedJsonlPath)) return new ReadResult { FileMissing = true };

        var records = new Dictionary<string, DerivedGymTrain>();
        var malformed = 0;
        string? errorMessage = null;

        try
        {
            foreach (var line in File.ReadLines(derivedJsonlPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<DerivedGymTrain>(line, JsonOptions);
                    if (record is not null) records[record.LogId] = record;
                }
                catch (JsonException)
                {
                    malformed++;
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Unable to read derived file '{derivedJsonlPath}': {ex.Message}";
            return new ReadResult { Records = records, MalformedLines = malformed, ErrorMessage = errorMessage };
        }

        return new ReadResult { Records = records, MalformedLines = malformed };
    }

    public sealed class ReadResult
    {
        public Dictionary<string, DerivedGymTrain> Records { get; init; } = new();
        public bool FileMissing { get; init; }
        public int MalformedLines { get; init; }
        public string? ErrorMessage { get; init; }
    }
}