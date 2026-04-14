using System.Text;
using System.Text.Json;

namespace HappyGymStats.Export;

/// <summary>
/// Flattens a JSON object into dotted-path key/value pairs suitable for CSV columns.
/// Also includes parent object keys with their value serialized as compact JSON,
/// so that nested structures like <c>details</c> appear both as a whole and as individual dotted paths.
/// </summary>
public static class JsonFlattener
{
    /// <summary>
    /// Flatten a JSON string into an ordered dictionary of dotted-path keys to string values.
    /// Parent object/array nodes are also included as keys with compact JSON values.
    /// </summary>
    public static Dictionary<string, string> Flatten(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        FlattenElement(doc.RootElement, prefix: string.Empty, result);
        return result;
    }

    /// <summary>
    /// Discover all dotted-path keys from a JSON string without collecting values.
    /// Returns a HashSet suitable for building a union-of-keys header.
    /// </summary>
    public static HashSet<string> DiscoverKeys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        DiscoverKeysFromElement(doc.RootElement, prefix: string.Empty, keys);
        return keys;
    }

    private static void FlattenElement(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                // Include the parent object key itself with compact JSON value.
                if (prefix.Length > 0)
                {
                    result[prefix] = JsonSerializer.Serialize(element);
                }

                foreach (var prop in element.EnumerateObject())
                {
                    var childPrefix = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenElement(prop.Value, childPrefix, result);
                }

                break;

            case JsonValueKind.Array:
                // Include the parent array key itself with compact JSON value.
                if (prefix.Length > 0)
                {
                    result[prefix] = JsonSerializer.Serialize(element);
                }

                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var childPrefix = $"{prefix}[{index}]";
                    FlattenElement(item, childPrefix, result);
                    index++;
                }

                break;

            default:
                result[prefix] = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString() ?? string.Empty,
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => string.Empty,
                    _ => element.GetRawText()
                };
                break;
        }
    }

    private static void DiscoverKeysFromElement(JsonElement element, string prefix, HashSet<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (prefix.Length > 0)
                {
                    keys.Add(prefix);
                }

                foreach (var prop in element.EnumerateObject())
                {
                    var childPrefix = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
                    DiscoverKeysFromElement(prop.Value, childPrefix, keys);
                }

                break;

            case JsonValueKind.Array:
                if (prefix.Length > 0)
                {
                    keys.Add(prefix);
                }

                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var childPrefix = $"{prefix}[{index}]";
                    DiscoverKeysFromElement(item, childPrefix, keys);
                    index++;
                }

                break;

            default:
                keys.Add(prefix);
                break;
        }
    }
}
