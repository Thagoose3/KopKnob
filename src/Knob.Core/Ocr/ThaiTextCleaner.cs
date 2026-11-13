using System.Text.RegularExpressions;

namespace Knob.Core.Ocr;

public static partial class ThaiTextCleaner
{
    // Space(s) with a Thai character (U+0E00–U+0E7F) on both sides: Tesseract inserts these inside Thai words.
    [GeneratedRegex(@"(?<=[฀-๿])[ \t]+(?=[฀-๿])")]
    private static partial Regex SpaceBetweenThai();

    [GeneratedRegex(@"[ \t]+(?=\n)")]
    private static partial Regex TrailingSpaces();

    /// <summary>
    /// Removes spaces between two Thai characters; keeps spaces next to digits/Latin.
    /// Also normalises line endings to \n and trims trailing whitespace.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n');
        t = SpaceBetweenThai().Replace(t, "");
        t = TrailingSpaces().Replace(t, "");
        return t.TrimEnd();
    }
}

public static class OcrPreprocess
{
    /// <summary>Upscale small selections more: Tesseract reads small screen text poorly.</summary>
    public static int ScaleFactor(int selectionHeightPx) => selectionHeightPx < 60 ? 3 : 2;

    /// <summary>Dark background (mean luminance below middle grey) → invert to dark-on-light.</summary>
    public static bool ShouldInvert(double meanLuma) => meanLuma < 128;
}
