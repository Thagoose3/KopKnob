using System.Runtime.InteropServices;

namespace Knob.App.Platform;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_DEVICECHANGE = 0x0219;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    /// <summary>GUID_ACDC_POWER_SOURCE: data is a DWORD, 0 = AC, 1 = battery, 2 = short-term (UPS).</summary>
    public static readonly Guid GUID_ACDC_POWER_SOURCE = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    /// <summary>For a PBT_POWERSETTINGCHANGE: true = on AC, false = on battery, null = another setting.</summary>
    public static bool? ReadAcDc(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return null;
        var setting = Marshal.PtrToStructure<Guid>(lParam);          // POWERBROADCAST_SETTING.PowerSetting
        if (setting != GUID_ACDC_POWER_SOURCE || Marshal.ReadInt32(lParam, 16) < 4) return null; // DataLength
        return Marshal.ReadInt32(lParam, 20) == 0;                       // Data
    }
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 2;
    private const short DBTF_NET = 2;

    /// <summary>
    /// Decode a WM_DEVICECHANGE broadcast for local volumes (sent to every top-level window): drive letters that
    /// arrived (true) or were removed (false). Network drives are ignored.
    /// </summary>
    public static IEnumerable<(char Drive, bool Arrived)> ReadVolumeChange(IntPtr wParam, IntPtr lParam)
    {
        var evt = wParam.ToInt64();
        if ((evt != DBT_DEVICEARRIVAL && evt != DBT_DEVICEREMOVECOMPLETE) || lParam == IntPtr.Zero) yield break;
        if (Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_VOLUME) yield break; // DEV_BROADCAST_HDR.dbch_devicetype
        var mask = Marshal.ReadInt32(lParam, 12);   // DEV_BROADCAST_VOLUME.dbcv_unitmask
        var flags = Marshal.ReadInt16(lParam, 16);  // dbcv_flags
        if ((flags & DBTF_NET) != 0) yield break;
        for (var i = 0; i < 26; i++)
            if ((mask & (1 << i)) != 0) yield return ((char)('A' + i), evt == DBT_DEVICEARRIVAL);
    }

    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public const uint SWP_NOZORDER = 0x0004;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static long GetWindowStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, -16).ToInt64() : GetWindowLong32(hwnd, -16);

    public static long GetExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hwnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hwnd, long style)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(style));
        else SetWindowLong32(hwnd, GWL_EXSTYLE, unchecked((int)style));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
