using System.Text.RegularExpressions;

namespace Knob.Core.Ocr;

public static partial class ThaiTextCleaner
{
    // Space(s) with a Thai character (U+0E00–U+0E7F) on both sides: Tesseract inserts these inside Thai words.
    [GeneratedRegex(@"(?<=[฀-๿])[ \t]+(?=[฀-๿])")]
    private static partial Regex SpaceBetweenThai();

    [GeneratedRegex(@"[ \t]+(?=\n)")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();

    /// <summary>
    /// Removes spaces between two Thai characters; keeps spaces next to digits/Latin.
    /// Also normalises line endings to \n, trims trailing whitespace per line, drops leading/trailing
    /// blank lines and collapses runs of blank lines to one.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n');
        t = SpaceBetweenThai().Replace(t, "");
        t = TrailingSpaces().Replace(t + "\n", "");
        t = BlankRuns().Replace(t, "\n\n");
        return t.Trim('\n', ' ', '\t');
    }
}

public static class OcrPreprocess
{
    private const long MaxOutputPixels = 6_000_000;

    /// <summary>
    /// ×4 upscale (Tesseract needs much bigger glyphs than screen text has; measured best on real Thai/English
    /// screen text), reduced for big selections so the image stays under ~6 MP and OCR stays fast. Never below ×1.
    /// </summary>
    public static int ScaleFactor(int width, int height)
    {
        var s = 4;
        while (s > 1 && (long)width * height * s * s > MaxOutputPixels) s--;
        return s;
    }

    /// <summary>Otsu's threshold for a 256-bin grey histogram. Empty or single-valued histogram → 128.</summary>
    public static int OtsuThreshold(int[] histogram)
    {
        long total = 0;
        double sum = 0;
        for (var i = 0; i < 256; i++)
        {
            total += histogram[i];
            sum += (double)i * histogram[i];
        }

        double sumB = 0, best = 0;
        long weightB = 0;
        var threshold = 128;
        for (var i = 0; i < 256; i++)
        {
            weightB += histogram[i];
            if (weightB == 0) continue;
            var weightF = total - weightB;
            if (weightF == 0) break;
            sumB += (double)i * histogram[i];
            var meanB = sumB / weightB;
            var meanF = (sum - sumB) / weightF;
            var between = (double)weightB * weightF * (meanB - meanF) * (meanB - meanF);
            if (between > best)
            {
                best = between;
                threshold = i;
            }
        }
        return threshold;
    }

    /// <summary>Dark background (mean luminance below middle grey) → invert to dark-on-light.</summary>
    public static bool ShouldInvert(double meanLuma) => meanLuma < 128;
}
