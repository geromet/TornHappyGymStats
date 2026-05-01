using System.Text;
using System.Text.Json;
using static HappyGymStats.Core.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// Persists derived gym-train reconstruction output as a JSONL sidecar file.
/// Writes are atomic (temp file + move/replace) to avoid leaving partial output.
/// </summary>
public static class DerivedGymTrainStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public sealed record WriteResult(bool Success, string? ErrorMessage, int RecordsWritten);

    public static WriteResult WriteAllAtomic(string outputJsonlPath, IEnumerable<DerivedGymTrain> derived)
    {
        if (string.IsNullOrWhiteSpace(outputJsonlPath))
            throw new ArgumentException("Output path must be provided.", nameof(outputJsonlPath));

        if (derived is null)
            throw new ArgumentNullException(nameof(derived));

        var dir = Path.GetDirectoryName(outputJsonlPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tempPath = outputJsonlPath + $".tmp-{Guid.NewGuid():N}";

        try
        {
            var written = 0;

            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var record in derived)
                {
                    var json = JsonSerializer.Serialize(record, JsonOptions);
                    writer.WriteLine(json);
                    written++;
                }

                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempPath, outputJsonlPath, overwrite: true);

            return new WriteResult(Success: true, ErrorMessage: null, RecordsWritten: written);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

            return new WriteResult(
                Success: false,
                ErrorMessage: $"Unable to write derived output '{outputJsonlPath}': {ex.Message}",
                RecordsWritten: 0);
        }
    }
}
