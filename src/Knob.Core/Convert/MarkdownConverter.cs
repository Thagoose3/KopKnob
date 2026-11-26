using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Knob.Core.Convert;

/// <summary>
/// Lightweight Markdown to standalone HTML5 (and plain text) converter with zero external dependencies.
/// Supports headings, fenced code blocks, tables, blockquotes, lists, horizontal rules, inline formatting and links.
/// </summary>
public static partial class MarkdownConverter
{
    public static string ToHtmlDocument(string markdown, string title = "Document")
    {
        var body = ToHtmlFragment(markdown);
        var safeTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Document" : title);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{safeTitle}}</title>
              <style>
                :root { color-scheme: light dark; }
                body {
                  max-width: 780px; margin: 2.5rem auto; padding: 0 1.25rem;
                  font: 16px/1.65 "Segoe UI", "Leelawadee UI", Tahoma, system-ui, sans-serif;
                  color: #1f2328; background: #ffffff;
                }
                @media (prefers-color-scheme: dark) {
                  body { color: #e6edf3; background: #0d1117; }
                  pre, code { background: #161b22 !important; border-color: #30363d !important; }
                  blockquote { border-color: #3b82f6; color: #9ca3af; }
                  th, td { border-color: #30363d !important; }
                  a { color: #58a6ff; }
                }
                h1, h2, h3, h4, h5, h6 { line-height: 1.3; margin-top: 1.5em; margin-bottom: 0.5em; }
                h1, h2 { border-bottom: 1px solid rgba(128,128,128,0.25); padding-bottom: 0.25em; }
                pre { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 8px; padding: 0.9rem 1rem; overflow-x: auto; }
                code { font-family: "Cascadia Mono", Consolas, monospace; font-size: 0.9em; background: #f6f8fa; padding: 0.15em 0.35em; border-radius: 4px; }
                pre code { background: none; padding: 0; }
                blockquote { margin: 1em 0; padding: 0.2em 1em; border-left: 4px solid #2563eb; color: #57606a; }
                table { border-collapse: collapse; width: 100%; margin: 1em 0; }
                th, td { border: 1px solid #d0d7de; padding: 0.45rem 0.7rem; text-align: left; }
                th { font-weight: 600; }
                img { max-width: 100%; height: auto; border-radius: 6px; }
                hr { border: 0; border-top: 1px solid rgba(128,128,128,0.3); margin: 1.8em 0; }
              </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }

    public static string ToHtmlFragment(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var lines = StripBom(markdown).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Fenced code block (``` or ~~~)
            if (TryStartFence(line, out var fence, out var lang))
            {
                i++;
                var code = new StringBuilder();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++; // closing fence
                var cls = string.IsNullOrEmpty(lang) ? "" : $" class=\"language-{WebUtility.HtmlEncode(lang)}\"";
                sb.Append("<pre><code").Append(cls).Append('>').Append(WebUtility.HtmlEncode(code.ToString())).AppendLine("</code></pre>");
                continue;
            }

            // ATX Heading (# .. ######)
            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                var content = heading.Groups[2].Value.TrimEnd('#', ' ');
                sb.Append($"<h{level}>{FormatInline(content)}</h{level}>\n");
                i++;
                continue;
            }

            // Horizontal rule
            if (HrRegex().IsMatch(line))
            {
                sb.AppendLine("<hr>");
                i++;
                continue;
            }

            // Pipe table
            if (i + 1 < lines.Length && line.Contains('|') && TableSeparatorRegex().IsMatch(lines[i + 1]))
            {
                var headers = SplitTableRow(line);
                var aligns = ParseTableAlignments(lines[i + 1], headers.Count);
                i += 2;
                sb.AppendLine("<table>");
                sb.Append("<thead><tr>");
                for (var c = 0; c < headers.Count; c++)
                    sb.Append($"<th{AlignAttr(aligns[c])}>{FormatInline(headers[c])}</th>");
                sb.AppendLine("</tr></thead>");
                sb.AppendLine("<tbody>");
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains('|'))
                {
                    var cells = SplitTableRow(lines[i]);
                    sb.Append("<tr>");
                    for (var c = 0; c < headers.Count; c++)
                    {
                        var val = c < cells.Count ? cells[c] : "";
                        sb.Append($"<td{AlignAttr(aligns[c])}>{FormatInline(val)}</td>");
                    }
                    sb.AppendLine("</tr>");
                    i++;
                }
                sb.AppendLine("</tbody></table>");
                continue;
            }

            // Blockquote
            if (line.TrimStart().StartsWith('>'))
            {
                var quoteLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    var q = lines[i].TrimStart()[1..];
                    if (q.StartsWith(' ')) q = q[1..];
                    quoteLines.Add(q);
                    i++;
                }
                sb.Append("<blockquote>\n").Append(ToHtmlFragment(string.Join("\n", quoteLines))).AppendLine("</blockquote>");
                continue;
            }

            // Unordered list
            if (UlItemRegex().IsMatch(line))
            {
                sb.AppendLine("<ul>");
                while (i < lines.Length && UlItemRegex().Match(lines[i]) is { Success: true } m)
                {
                    sb.Append("<li>").Append(FormatInline(m.Groups[1].Value.Trim())).AppendLine("</li>");
                    i++;
                }
                sb.AppendLine("</ul>");
                continue;
            }

            // Ordered list
            if (OlItemRegex().IsMatch(line))
            {
                sb.AppendLine("<ol>");
                while (i < lines.Length && OlItemRegex().Match(lines[i]) is { Success: true } m)
                {
                    sb.Append("<li>").Append(FormatInline(m.Groups[1].Value.Trim())).AppendLine("</li>");
                    i++;
                }
                sb.AppendLine("</ol>");
                continue;
            }

            // Paragraph
            var para = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines, i))
            {
                para.Add(lines[i].Trim());
                i++;
            }
            if (para.Count == 0)
            {
                para.Add(lines[i].Trim());
                i++;
            }
            sb.Append("<p>").Append(string.Join("<br>\n", para.Select(FormatInline))).AppendLine("</p>");
        }

        return sb.ToString();
    }

    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var lines = StripBom(markdown).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        var inFence = false;
        var fenceToken = "";

        foreach (var raw in lines)
        {
            if (!inFence && TryStartFence(raw, out fenceToken, out _))
            {
                inFence = true;
                continue;
            }
            if (inFence)
            {
                if (raw.TrimStart().StartsWith(fenceToken, StringComparison.Ordinal))
                    inFence = false;
                else
                    sb.AppendLine(raw);
                continue;
            }
            if (HrRegex().IsMatch(raw))
            {
                sb.AppendLine("----------------------------------------");
                continue;
            }

            var line = HeadingRegex().Replace(raw, "$2");
            line = Regex.Replace(line, @"^\s*>\s?", "");
            line = UlItemRegex().Replace(line, "• $1");
            line = ImageInlineRegex().Replace(line, "$1");
            line = LinkInlineRegex().Replace(line, "$1 ($2)");
            line = InlineCodeRegex().Replace(line, "$1");
            line = BoldRegex().Replace(line, "$2");
            line = StrikeRegex().Replace(line, "$1");
            line = ItalicStarRegex().Replace(line, "$1");
            line = ItalicUnderscoreRegex().Replace(line, "$1");
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static bool StartsBlock(string[] lines, int i)
    {
        var l = lines[i];
        return TryStartFence(l, out _, out _)
               || HeadingRegex().IsMatch(l)
               || HrRegex().IsMatch(l)
               || l.TrimStart().StartsWith('>')
               || UlItemRegex().IsMatch(l)
               || OlItemRegex().IsMatch(l)
               || (i + 1 < lines.Length && l.Contains('|') && TableSeparatorRegex().IsMatch(lines[i + 1]));
    }

    private static bool TryStartFence(string line, out string fence, out string lang)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
        {
            var ch = trimmed[0];
            var len = 0;
            while (len < trimmed.Length && trimmed[len] == ch) len++;
            fence = new string(ch, len);
            lang = trimmed[len..].Trim();
            return true;
        }
        fence = lang = "";
        return false;
    }

    private static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    private static List<string?> ParseTableAlignments(string line, int count)
    {
        var raw = SplitTableRow(line);
        var result = new List<string?>(count);
        for (var i = 0; i < count; i++)
        {
            var cell = i < raw.Count ? raw[i] : "";
            var left = cell.StartsWith(':');
            var right = cell.EndsWith(':');
            result.Add(left && right ? "center" : right ? "right" : left ? "left" : null);
        }
        return result;
    }

    private static string AlignAttr(string? align) => align is null ? "" : $" style=\"text-align:{align}\"";

    private static string FormatInline(string text)
    {
        // Stash inline code spans and links/images behind placeholders so emphasis regexes never mangle URLs or code.
        var tokens = new List<string>();
        string Stash(string html)
        {
            var key = $"\u0001T{tokens.Count}\u0001";
            tokens.Add(html);
            return key;
        }

        var work = InlineCodeRegex().Replace(text, m => Stash($"<code>{WebUtility.HtmlEncode(m.Groups[1].Value)}</code>"));
        work = ImageInlineRegex().Replace(work, m =>
        {
            var alt = WebUtility.HtmlEncode(m.Groups[1].Value);
            var url = SafeUrl(m.Groups[2].Value);
            return url is null ? alt : Stash($"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"{alt}\">");
        });
        work = LinkInlineRegex().Replace(work, m =>
        {
            var label = FormatEmphasis(WebUtility.HtmlEncode(m.Groups[1].Value));
            var url = SafeUrl(m.Groups[2].Value);
            return url is null ? label : Stash($"<a href=\"{WebUtility.HtmlEncode(url)}\">{label}</a>");
        });

        work = FormatEmphasis(WebUtility.HtmlEncode(work));

        // Newest first: a later token (a link) can contain an earlier one (inline code in its label).
        for (var i = tokens.Count - 1; i >= 0; i--)
            work = work.Replace($"\u0001T{i}\u0001", tokens[i], StringComparison.Ordinal);

        return work;
    }

    private static string FormatEmphasis(string htmlEncoded)
    {
        var s = BoldRegex().Replace(htmlEncoded, "<strong>$2</strong>");
        s = StrikeRegex().Replace(s, "<del>$1</del>");
        s = ItalicStarRegex().Replace(s, "<em>$1</em>");
        s = ItalicUnderscoreRegex().Replace(s, "<em>$1</em>");
        return s;
    }

    private static string? SafeUrl(string raw)
    {
        var u = raw.Trim();
        if (u.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || u.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            || u.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
            return null;
        return u;
    }

    private static string StripBom(string s) => s.Length > 0 && s[0] == '\uFEFF' ? s[1..] : s;

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.+?)\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s{0,3}([-*_])(?:\s*\1){2,}\s*$")]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"^\s*\|?(?:\s*:?-{3,}:?\s*\|)+\s*:?-{3,}:?\s*\|?\s*$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+(.+)$")]
    private static partial Regex UlItemRegex();

    [GeneratedRegex(@"^\s{0,3}\d+\.\s+(.+)$")]
    private static partial Regex OlItemRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex ImageInlineRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkInlineRegex();

    [GeneratedRegex(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~")]
    private static partial Regex StrikeRegex();

    [GeneratedRegex(@"(?<!\*)\*(?=\S)([^*]+?)(?<=\S)\*(?!\*)")]
    private static partial Regex ItalicStarRegex();

    [GeneratedRegex(@"(?<![\w_])_(?=\S)([^_]+?)(?<=\S)_(?![\w_])")]
    private static partial Regex ItalicUnderscoreRegex();
}
