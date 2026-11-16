using System.Management;

namespace Knob.App.Platform;

/// <summary>
/// Brightness of the built-in (laptop) display through WMI. External monitors don't support this (they would
/// need DDC/CI), so <see cref="Get"/> returns null there. WMI calls take tens of milliseconds: call off the UI thread.
/// </summary>
internal static class ScreenBrightness
{
    private const string Scope = @"root\WMI";

    /// <summary>0–100, or null when no display supports it.</summary>
    public static int? Get()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
            {
                using (o) return Convert.ToInt32(o["CurrentBrightness"]);
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    public static bool Set(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            using var results = searcher.Get();
            var any = false;
            foreach (ManagementObject o in results)
            {
                using (o) o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)Math.Clamp(percent, 0, 100)]);
                any = true;
            }
            return any;
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
