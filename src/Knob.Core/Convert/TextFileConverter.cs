using System.Text;

namespace Knob.Core.Convert;

/// <summary>
/// File-level entry point for structured data (JSON ↔ CSV/TSV) and Markdown (MD → HTML/TXT) conversions.
/// Writes atomically via a temporary file; CSV and TSV include a UTF-8 BOM for Microsoft Excel compatibility.
/// </summary>
public static class TextFileConverter
{
    public static void Convert(string inputPath, string outputPath, ConvertFormat target)
    {
        var input = ReadText(inputPath);
        var srcExt = Path.GetExtension(inputPath).ToLowerInvariant();
        var title = Path.GetFileNameWithoutExtension(inputPath);

        var (content, encoding) = (srcExt, target.Id) switch
        {
            (".json", "csv") => (TabularConverter.JsonToDelimited(input, ','), TabularConverter.Utf8Bom),
            (".json", "tsv") => (TabularConverter.JsonToDelimited(input, '\t'), TabularConverter.Utf8Bom),
            (".csv", "json") => (TabularConverter.DelimitedToJson(input, ','), TabularConverter.Utf8NoBom),
            (".tsv", "json") => (TabularConverter.DelimitedToJson(input, '\t'), TabularConverter.Utf8NoBom),
            (".csv", "tsv") => (TabularConverter.ConvertDelimiter(input, ',', '\t'), TabularConverter.Utf8Bom),
            (".tsv", "csv") => (TabularConverter.ConvertDelimiter(input, '\t', ','), TabularConverter.Utf8Bom),
            (".md" or ".markdown", "html") => (MarkdownConverter.ToHtmlDocument(input, title), TabularConverter.Utf8NoBom),
            (".md" or ".markdown", "txt") => (MarkdownConverter.ToPlainText(input), TabularConverter.Utf8NoBom),
            _ => throw new NotSupportedException($"Cannot convert '{srcExt}' to '{target.Label}'."),
        };

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = outputPath + ".tmp";
        try
        {
            File.WriteAllText(tmp, content, encoding);
            File.Move(tmp, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Read a text file honouring a BOM (UTF-8/UTF-16/UTF-32). Without one, use UTF-8 if the bytes are valid UTF-8;
    /// otherwise assume Windows-874 (Thai): that is what Excel's plain "CSV (Comma delimited)" writes on Thai Windows.
    /// </summary>
    internal static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using (var reader = new StreamReader(new MemoryStream(bytes), StrictUtf8, detectEncodingFromByteOrderMarks: true))
        {
            try
            {
                return reader.ReadToEnd();
            }
            catch (DecoderFallbackException)
            {
                // not UTF-8 — fall through to the legacy Thai code page
            }
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(874).GetString(bytes);
    }
}
