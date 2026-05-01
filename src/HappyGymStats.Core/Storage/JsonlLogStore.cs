using System.Text;
using System.Text.Json;

namespace HappyGymStats.Core.Storage;

public static class JsonlLogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record ScanResult(
        HashSet<string> ExistingIds,
        string? LastLogId,
        DateTimeOffset? LastLogTimestamp,
        string? LastLogTitle,
        string? LastLogCategory,
        int LinesRead,
        int MalformedLines);

    public static void Append<T>(string jsonlPath, IEnumerable<T> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jsonlPath) ?? ".");

        using var fs = new FileStream(jsonlPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var record in records)
        {
            var json = JsonSerializer.Serialize(record, JsonOptions);
            writer.WriteLine(json);
        }
    }

    public static ScanResult ScanAndQuarantine(string jsonlPath, string quarantineDir)
    {
        var ids = new HashSet<string>();
        string? lastId = null;
        DateTimeOffset? lastTs = null;
        string? lastTitle = null;
        string? lastCategory = null;
        var malformed = 0;
        var linesRead = 0;

        if (!File.Exists(jsonlPath))
        {
            return new ScanResult(
                ExistingIds: ids,
                LastLogId: null,
                LastLogTimestamp: null,
                LastLogTitle: null,
                LastLogCategory: null,
                LinesRead: 0,
                MalformedLines: 0);
        }

        Directory.CreateDirectory(quarantineDir);

        using var fs = new FileStream(jsonlPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: true);

        string? line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            linesRead++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!TryExtractId(root, out var id))
                    throw new JsonException("Missing 'id' field.");

                ids.Add(id);

                lastId = id;
                lastTs = TryExtractTimestamp(root);
                lastTitle = TryExtractString(root, "title") ?? TryExtractNestedString(root, "details", "title");
                lastCategory = TryExtractString(root, "category") ?? TryExtractNestedString(root, "details", "category") ?? TryExtractString(root, "type");
            }
            catch (JsonException ex)
            {
                malformed++;

                var isLastLine = reader.EndOfStream;
                if (isLastLine)
                {
                    QuarantinePartialFinalLineBytes(jsonlPath, quarantineDir, lineNumber, ex);
                    TruncateToLastValidNewline(jsonlPath);
                }
                else
                {
                    QuarantineMalformedLineText(quarantineDir, jsonlPath, lineNumber, line, ex);
                }
            }
        }

        return new ScanResult(
            ExistingIds: ids,
            LastLogId: lastId,
            LastLogTimestamp: lastTs,
            LastLogTitle: lastTitle,
            LastLogCategory: lastCategory,
            LinesRead: linesRead,
            MalformedLines: malformed);
    }

    private static bool TryExtractId(JsonElement root, out string id)
    {
        id = "";

        if (TryGetPropertyIgnoreCase(root, "id", out var value) || TryGetPropertyIgnoreCase(root, "log_id", out value))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    id = s;
                    return true;
                }
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
            {
                id = n.ToString();
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset? TryExtractTimestamp(JsonElement root)
    {
        if (TryGetPropertyIgnoreCase(root, "timestamp", out var ts) || TryGetPropertyIgnoreCase(root, "time", out ts))
        {
            if (ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var unixSeconds))
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
                catch { return null; }
            }

            if (ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var dto))
                return dto;
        }

        return null;
    }

    private static string? TryExtractString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? TryExtractNestedString(JsonElement root, string parent, string child)
    {
        if (!TryGetPropertyIgnoreCase(root, parent, out var parentEl) || parentEl.ValueKind != JsonValueKind.Object)
            return null;

        return TryExtractString(parentEl, child);
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

    private static void QuarantineMalformedLineText(
        string quarantineDir,
        string jsonlPath,
        int lineNumber,
        string rawLine,
        Exception ex)
    {
        var baseName = Path.GetFileName(jsonlPath);
        var quarantinePath = Path.Combine(quarantineDir, $"{baseName}.line-{lineNumber:00000000}.malformed.txt");

        var payload = new StringBuilder();
        payload.AppendLine($"source={jsonlPath}");
        payload.AppendLine($"lineNumber={lineNumber}");
        payload.AppendLine($"error={ex.GetType().Name}: {ex.Message}");
        payload.AppendLine("---");
        payload.AppendLine(rawLine);

        File.WriteAllText(quarantinePath, payload.ToString());
    }

    private static void QuarantinePartialFinalLineBytes(string jsonlPath, string quarantineDir, int lineNumber, Exception ex)
    {
        var baseName = Path.GetFileName(jsonlPath);
        var quarantineBytesPath = Path.Combine(quarantineDir, $"{baseName}.line-{lineNumber:00000000}.partial-final.bin");
        var quarantineMetaPath = Path.Combine(quarantineDir, $"{baseName}.line-{lineNumber:00000000}.partial-final.txt");

        var bytes = File.ReadAllBytes(jsonlPath);
        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        var start = lastNewline >= 0 ? lastNewline + 1 : 0;

        var tail = bytes.AsSpan(start).ToArray();
        File.WriteAllBytes(quarantineBytesPath, tail);

        var meta = new StringBuilder();
        meta.AppendLine($"source={jsonlPath}");
        meta.AppendLine($"lineNumber={lineNumber}");
        meta.AppendLine($"error={ex.GetType().Name}: {ex.Message}");
        meta.AppendLine($"bytesLength={tail.Length}");
        meta.AppendLine("note=Final line was not valid JSON; file was truncated back to the last valid newline.");
        File.WriteAllText(quarantineMetaPath, meta.ToString());
    }

    private static void TruncateToLastValidNewline(string jsonlPath)
    {
        var bytes = File.ReadAllBytes(jsonlPath);
        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        var newLength = lastNewline >= 0 ? lastNewline + 1 : 0;

        using var fs = new FileStream(jsonlPath, FileMode.Open, FileAccess.Write, FileShare.Read);
        fs.SetLength(newLength);
    }
}
