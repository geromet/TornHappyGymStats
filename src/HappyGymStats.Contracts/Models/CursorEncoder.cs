using System.Text;
using System.Text.Json;

namespace HappyGymStats.Core.Models;

public static class CursorEncoder
{
    public static bool TryDecode(string? value, out PageCursor? cursor)
    {
        cursor = null;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(value));
            cursor = JsonSerializer.Deserialize<PageCursor>(json);
            return cursor is not null && !string.IsNullOrWhiteSpace(cursor.Id);
        }
        catch (FormatException) { return false; }
        catch (JsonException) { return false; }
    }

    public static string Encode(PageCursor cursor)
        => Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor)));

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
