using System.Text;

namespace HappyGymStats.Legacy.Cli.Export;

/// <summary>
///     Writes CSV rows with RFC 4180 compliant escaping:
///     fields containing commas, double quotes, or newlines are quoted;
///     embedded double quotes are escaped by doubling them.
/// </summary>
public static class CsvWriter
{
    private static readonly char[] CharsRequiringQuoting = { ',', '"', '\r', '\n' };

    /// <summary>
    ///     Write a CSV header line (column names) followed by a newline.
    /// </summary>
    public static void WriteHeader(StreamWriter writer, IReadOnlyList<string> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) writer.Write(',');
            WriteField(writer, columns[i]);
        }

        writer.WriteLine();
    }

    /// <summary>
    ///     Write a single CSV data row. Values are matched to <paramref name="columns" /> by key lookup
    ///     in <paramref name="row" />; missing values produce empty fields.
    /// </summary>
    public static void WriteRow(StreamWriter writer, IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, string> row)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) writer.Write(',');

            if (row.TryGetValue(columns[i], out var value)) WriteField(writer, value ?? string.Empty);
        }

        writer.WriteLine();
    }

    /// <summary>
    ///     Write a single CSV field with quoting/escaping as needed.
    /// </summary>
    public static void WriteField(StreamWriter writer, string value)
    {
        if (value.AsSpan().IndexOfAny(CharsRequiringQuoting) >= 0)
        {
            writer.Write('"');
            // Replace " with ""
            var span = value.AsSpan();
            var start = 0;
            while (start < span.Length)
            {
                var quoteIndex = span.Slice(start).IndexOf('"');
                if (quoteIndex < 0)
                {
                    writer.Write(span.Slice(start));
                    break;
                }

                writer.Write(span.Slice(start, quoteIndex));
                writer.Write("\"\"");
                start += quoteIndex + 1;
            }

            writer.Write('"');
        }
        else
        {
            writer.Write(value);
        }
    }

    /// <summary>
    ///     Escape a single CSV field value and return the result as a string.
    ///     Useful for testing and verification without a StreamWriter.
    /// </summary>
    public static string EscapeField(string value)
    {
        if (value.AsSpan().IndexOfAny(CharsRequiringQuoting) < 0)
            return value;

        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');

        var span = value.AsSpan();
        var start = 0;
        while (start < span.Length)
        {
            var quoteIndex = span.Slice(start).IndexOf('"');
            if (quoteIndex < 0)
            {
                sb.Append(span.Slice(start));
                break;
            }

            sb.Append(span.Slice(start, quoteIndex));
            sb.Append("\"\"");
            start += quoteIndex + 1;
        }

        sb.Append('"');
        return sb.ToString();
    }
}