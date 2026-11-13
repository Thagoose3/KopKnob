using System.Globalization;

namespace Knob.Core.Storage;

public static class ByteFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>1024-based; one decimal below 10, whole numbers above. Always invariant culture.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024) return $"{Math.Max(0, bytes)} B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var text = value < 10
            ? Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture)
            : Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        return $"{text} {Units[unit]}";
    }
}
