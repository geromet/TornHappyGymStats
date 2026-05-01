using System.Text;
using System.Text.Json;

namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// Streaming, read-only JSONL reader for Torn user logs.
/// </summary>
public static class JsonlLogReader
{
    public sealed record LogRecord(
        string LogId,
        DateTimeOffset OccurredAtUtc,
        string? Title,
        string? Category,
        string RawJson)
    {
        public JsonDocument ParseJsonDocument() => JsonDocument.Parse(RawJson);
    }

    public sealed class ReadStats
    {
        public int LinesRead { get; internal set; }
        public int BlankLines { get; internal set; }
        public int MalformedLines { get; internal set; }
        public int RecordsYielded { get; internal set; }
    }

    public sealed record ReadResult(
        IEnumerable<LogRecord> Records,
        ReadStats Stats,
        bool Success,
        string? ErrorMessage);

    public static ReadResult Read(string jsonlPath)
    {
        var stats = new ReadStats();

        if (!File.Exists(jsonlPath))
        {
            return new ReadResult(
                Records: Enumerable.Empty<LogRecord>(),
                Stats: stats,
                Success: false,
                ErrorMessage: $"Log file not found: {jsonlPath}");
        }

        try
        {
            using var _ = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (Exception ex)
        {
            return new ReadResult(
                Records: Enumerable.Empty<LogRecord>(),
                Stats: stats,
                Success: false,
                ErrorMessage: $"Unable to read log file '{jsonlPath}': {ex.Message}");
        }

        IEnumerable<LogRecord> Iterator()
        {
            using var fs = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(
                fs,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: false);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                stats.LinesRead++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    stats.BlankLines++;
                    continue;
                }

                if (!TryParseRecord(line, out var record))
                {
                    stats.MalformedLines++;
                    continue;
                }

                stats.RecordsYielded++;
                yield return record;
            }
        }

        return new ReadResult(
            Records: Iterator(),
            Stats: stats,
            Success: true,
            ErrorMessage: null);
    }

    private static bool TryParseRecord(string rawJson, out LogRecord record)
    {
        record = default!;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!TryExtractId(root, out var id) || !TryExtractTimestampUtc(root, out var occurredAtUtc))
                return false;

            var title = TryExtractString(root, "title") ?? TryExtractNestedString(root, "details", "title");
            var category = TryExtractString(root, "category") ?? TryExtractNestedString(root, "details", "category") ?? TryExtractString(root, "type");

            record = new LogRecord(
                LogId: id,
                OccurredAtUtc: occurredAtUtc,
                Title: title,
                Category: category,
                RawJson: rawJson);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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

    private static bool TryExtractTimestampUtc(JsonElement root, out DateTimeOffset occurredAtUtc)
    {
        occurredAtUtc = default;

        if (TryGetPropertyIgnoreCase(root, "timestamp", out var ts) || TryGetPropertyIgnoreCase(root, "time", out ts))
        {
            if (ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var unixSeconds))
            {
                try
                {
                    occurredAtUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var dto))
            {
                occurredAtUtc = dto.ToUniversalTime();
                return true;
            }
        }

        return false;
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
}
