using System.Text.Json;
using HappyGymStats.Storage.Models;

namespace HappyGymStats.Storage;

public static class CheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static Checkpoint? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<Checkpoint>(json, JsonOptions);
        }
        catch
        {
            // Status rendering should never crash the app.
            return null;
        }
    }

    /// <summary>
    /// Write checkpoint JSON atomically.
    /// </summary>
    public static void Write(string path, Checkpoint checkpoint)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);

        // Avoid partially overwriting checkpoint.json.
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }
}
