using System.Text.Json;
using HappyGymStats.Reconstruction;

using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Export;

public static class DerivedHappyEventReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public sealed class ReadResult
    {
        public Dictionary<string, DerivedHappyEvent> BySourceLogId { get; init; } = new();
        public List<DerivedHappyEvent> AllEvents { get; init; } = new();
        public bool FileMissing { get; init; }
        public int MalformedLines { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public static ReadResult Read(string derivedJsonlPath)
    {
        if (!File.Exists(derivedJsonlPath))
            return new ReadResult { FileMissing = true };

        var byLogId = new Dictionary<string, DerivedHappyEvent>(StringComparer.Ordinal);
        var all = new List<DerivedHappyEvent>();
        var malformed = 0;

        try
        {
            foreach (var line in File.ReadLines(derivedJsonlPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<DerivedHappyEvent>(line, JsonOptions);
                    if (record is null)
                        continue;

                    all.Add(record);

                    if (!string.IsNullOrWhiteSpace(record.SourceLogId))
                    {
                        // Latest-write-wins by log id (should be unique anyway).
                        byLogId[record.SourceLogId] = record;
                    }
                }
                catch (JsonException)
                {
                    malformed++;
                }
            }
        }
        catch (Exception ex)
        {
            return new ReadResult
            {
                BySourceLogId = byLogId,
                AllEvents = all,
                MalformedLines = malformed,
                ErrorMessage = $"Unable to read derived happy events file '{derivedJsonlPath}': {ex.Message}",
            };
        }

        return new ReadResult { BySourceLogId = byLogId, AllEvents = all, MalformedLines = malformed };
    }
}
