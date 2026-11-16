using System.Text.RegularExpressions;

namespace Knob.Core.Devices;

/// <summary>
/// Pulls the 12-hex-digit Bluetooth address out of a device id, so a connected device (association-endpoint id,
/// "BluetoothLE#BluetoothLE&lt;adapter&gt;-&lt;device&gt;") can be matched to its device node ("BTHLE\DEV_…"), where
/// Windows keeps the battery level.
/// </summary>
public static partial class BluetoothAddress
{
    public static string? FromId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var m = DevNode().Match(id);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

        m = Endpoint().Match(id);
        if (m.Success) return m.Groups[1].Value.Replace(":", "").ToUpperInvariant();

        m = Service().Match(id);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"DEV_([0-9A-F]{12})(?![0-9A-F])", RegexOptions.IgnoreCase)]
    private static partial Regex DevNode();

    [GeneratedRegex(@"^Bluetooth(?:LE)?#.*-((?:[0-9A-F]{2}:){5}[0-9A-F]{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex Endpoint();

    [GeneratedRegex(@"^BTH.*&([0-9A-F]{12})_C[0-9A-F]+$", RegexOptions.IgnoreCase)]
    private static partial Regex Service();
}

/// <summary>Mouse-wheel volume steps: 2% per wheel notch (120), proportionally for touchpads.</summary>
public static class VolumeStep
{
    public const float PerNotch = 0.02f;

    public static float Apply(float level, int wheelDelta) =>
        Math.Clamp(level + wheelDelta / 120f * PerNotch, 0f, 1f);

    public static int Percent(float level) => (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100, MidpointRounding.AwayFromZero);
}
