using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Knob.Core.Convert;

/// <summary>
/// Converts between JSON and CSV/TSV. CSV/TSV output includes a UTF-8 BOM so Microsoft Excel on Windows
/// opens Thai and other Unicode text cleanly; JSON output keeps Unicode characters unescaped.
/// </summary>
public static class TabularConverter
{
    public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonWriterOptions PrettyJson = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string JsonToDelimited(string json, char delimiter = ',')
    {
        using var doc = JsonDocument.Parse(StripBom(json));
        var root = doc.RootElement;

        return root.ValueKind switch
        {
            JsonValueKind.Array => ArrayToDelimited(root, delimiter),
            JsonValueKind.Object => ObjectToDelimited(root, delimiter),
            _ => throw new InvalidDataException("JSON root must be an array or an object."),
        };
    }

    public static string DelimitedToJson(string text, char delimiter = ',')
    {
        var rows = ParseDelimited(StripBom(text), delimiter);
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty))
            rows.RemoveAt(rows.Count - 1);

        if (rows.Count == 0) return "[]";

        var headers = BuildHeaders(rows[0]);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, PrettyJson))
        {
            writer.WriteStartArray();
            for (var r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 1 && row[0].Length == 0 && headers.Count > 1) continue;

                writer.WriteStartObject();
                var count = Math.Max(headers.Count, row.Count);
                for (var c = 0; c < count; c++)
                {
                    var key = c < headers.Count ? headers[c] : $"column_{c + 1}";
                    var cell = c < row.Count ? row[c] : "";
                    writer.WritePropertyName(key);
                    WriteCellValue(writer, cell);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string ConvertDelimiter(string text, char fromDelimiter, char toDelimiter)
    {
        var rows = ParseDelimited(StripBom(text), fromDelimiter);
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty))
            rows.RemoveAt(rows.Count - 1);
        return FormatRows(rows, toDelimiter);
    }

    /// <summary>RFC 4180 parser supporting quoted fields, escaped quotes (""), and multi-line cells.</summary>
    public static List<List<string>> ParseDelimited(string text, char delimiter = ',')
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;

        var currentRow = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(ch);
                }
                continue;
            }

            if (ch == '"' && cell.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                currentRow.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                currentRow.Add(cell.ToString());
                cell.Clear();
                rows.Add(currentRow);
                currentRow = [];
            }
            else
            {
                cell.Append(ch);
            }
        }

        if (inQuotes || cell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(cell.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static string ArrayToDelimited(JsonElement array, char delimiter)
    {
        var items = array.EnumerateArray().ToList();
        if (items.Count == 0) return "";

        if (items.All(e => e.ValueKind == JsonValueKind.Object))
        {
            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obj in items)
            {
                foreach (var prop in obj.EnumerateObject())
                {
                    if (seen.Add(prop.Name)) columns.Add(prop.Name);
                }
            }

            var rows = new List<IReadOnlyList<string>>(items.Count + 1) { columns };
            foreach (var obj in items)
            {
                var row = new string[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                {
                    row[i] = obj.TryGetProperty(columns[i], out var val) ? ElementToCell(val) : "";
                }
                rows.Add(row);
            }
            return FormatRows(rows, delimiter);
        }

        if (items.All(e => e.ValueKind == JsonValueKind.Array))
        {
            var rows = items
                .Select(a => (IReadOnlyList<string>)a.EnumerateArray().Select(ElementToCell).ToList())
                .ToList();
            return FormatRows(rows, delimiter);
        }

        var scalarRows = new List<IReadOnlyList<string>>(items.Count + 1) { new[] { "value" } };
        foreach (var item in items) scalarRows.Add([ElementToCell(item)]);
        return FormatRows(scalarRows, delimiter);
    }

    private static string ObjectToDelimited(JsonElement obj, char delimiter)
    {
        // If the object wraps a single array property (e.g. { "items": [ ... ] }), unwrap that table.
        var props = obj.EnumerateObject().ToList();
        if (props.Count == 1 && props[0].Value.ValueKind == JsonValueKind.Array)
            return ArrayToDelimited(props[0].Value, delimiter);

        var headers = props.Select(p => p.Name).ToList();
        var values = props.Select(p => ElementToCell(p.Value)).ToList();
        return FormatRows([headers, values], delimiter);
    }

    private static string ElementToCell(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => el.GetRawText(),
        _ => el.GetRawText(),
    };

    private static List<string> BuildHeaders(IReadOnlyList<string> raw)
    {
        var result = new List<string>(raw.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < raw.Count; i++)
        {
            var baseName = string.IsNullOrWhiteSpace(raw[i]) ? $"column_{i + 1}" : raw[i].Trim();
            var name = baseName;
            for (var suffix = 2; !used.Add(name); suffix++)
                name = $"{baseName}_{suffix}";
            result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// Preserve strings with leading zeros (e.g. Thai phone numbers "0812345678" or zip/ID codes) as strings;
    /// convert canonical numbers and booleans to native JSON primitives.
    /// </summary>
    private static void WriteCellValue(Utf8JsonWriter writer, string cell)
    {
        if (cell.Length == 0)
        {
            writer.WriteNullValue();
            return;
        }
        if (cell.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteBooleanValue(true);
            return;
        }
        if (cell.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteBooleanValue(false);
            return;
        }
        if (LooksLikeCanonicalNumber(cell))
        {
            if (long.TryParse(cell, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
            {
                writer.WriteNumberValue(l);
                return;
            }
            if (decimal.TryParse(cell, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
            {
                writer.WriteNumberValue(d);
                return;
            }
        }
        writer.WriteStringValue(cell);
    }

    private static bool LooksLikeCanonicalNumber(string s)
    {
        var span = s.AsSpan();
        if (span.Length == 0) return false;
        if (span[0] == '-') span = span[1..];
        if (span.Length == 0 || !char.IsAsciiDigit(span[0])) return false;
        // "0" and "0.5" are numbers; "0812345678" or "007" have leading zeros and must stay strings.
        if (span[0] == '0' && span.Length > 1 && span[1] != '.') return false;

        var dots = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '.')
            {
                if (++dots > 1 || i == span.Length - 1) return false;
            }
            else if (!char.IsAsciiDigit(span[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static string FormatRows(IReadOnlyList<IReadOnlyList<string>> rows, char delimiter)
    {
        var sb = new StringBuilder();
        for (var r = 0; r < rows.Count; r++)
        {
            if (r > 0) sb.Append("\r\n");
            var row = rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                if (c > 0) sb.Append(delimiter);
                sb.Append(EscapeCell(row[c] ?? "", delimiter));
            }
        }
        return sb.ToString();
    }

    private static string EscapeCell(string value, char delimiter)
    {
        var mustQuote = value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0
                        || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));
        if (!mustQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string StripBom(string s) => s.Length > 0 && s[0] == '\uFEFF' ? s[1..] : s;
}
