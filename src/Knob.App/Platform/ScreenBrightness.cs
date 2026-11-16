using System.Management;
using System.Runtime.InteropServices;

namespace Knob.App.Platform;

/// <summary>
/// Screen brightness: the built-in (laptop) display through WMI, external monitors through DDC/CI (dxva2).
/// Setting changes every display that supports either; reading prefers the built-in display.
/// Both are slow (tens of milliseconds per display): call off the UI thread.
/// </summary>
internal static class ScreenBrightness
{
    private const string Scope = @"root\WMI";

    /// <summary>0–100, or null when no display supports it.</summary>
    public static int? Get() => GetWmi() ?? GetDdc();

    public static bool Set(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var wmi = SetWmi(percent);
        var ddc = SetDdc(percent);
        return wmi || ddc;
    }

    // ───────── built-in display (WMI) ─────────

    private static int? GetWmi()
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
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    private static bool SetWmi(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            using var results = searcher.Get();
            var any = false;
            foreach (ManagementObject o in results)
            {
                using (o) o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)percent]);
                any = true;
            }
            return any;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ───────── external monitors (DDC/CI) ─────────

    private static int? GetDdc()
    {
        int? result = null;
        ForEachPhysicalMonitor(h =>
        {
            if (result is null && GetMonitorBrightness(h, out var min, out var cur, out var max) && max > min)
                result = (int)Math.Round((cur - min) * 100.0 / (max - min));
        });
        return result;
    }

    private static bool SetDdc(int percent)
    {
        var any = false;
        ForEachPhysicalMonitor(h =>
        {
            if (!GetMonitorBrightness(h, out var min, out _, out var max) || max <= min) return;
            any |= SetMonitorBrightness(h, (uint)Math.Round(min + (max - min) * percent / 100.0));
        });
        return any;
    }

    private static void ForEachPhysicalMonitor(Action<IntPtr> action)
    {
        var monitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) => { monitors.Add(h); return true; }, IntPtr.Zero);
        foreach (var hmon in monitors)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out var n) || n == 0) continue;
            var physical = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(hmon, n, physical)) continue;
            try
            {
                foreach (var p in physical) action(p.hPhysicalMonitor);
            }
            finally
            {
                DestroyPhysicalMonitors(n, physical);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);
}
