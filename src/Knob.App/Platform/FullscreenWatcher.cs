using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace Knob.App.Platform;

/// <summary>
/// Tells when an app goes fullscreen on the primary monitor, without polling. Two event sources, both just
/// trigger a re-check of the foreground window:
/// <list type="bullet">
/// <item>EVENT_SYSTEM_FOREGROUND (WinEvent hook) — the foreground window changed;</item>
/// <item>EVENT_OBJECT_LOCATIONCHANGE, hooked only for the foreground window's process and filtered to that
/// window — it resized itself to fullscreen and back (F11) without the foreground changing;</item>
/// <item>ABN_FULLSCREENAPP from a hidden appbar (ABM_NEW only, reserves no space) — catches an app going
/// fullscreen in place (F11). On some machines the shell stops sending it (e.g. an always-on-top overlay counts as
/// "already fullscreen"), which is why it is not the only source.</item>
/// </list>
/// Re-registers the appbar when Explorer restarts (TaskbarCreated). Unhooks/unregisters on Dispose.
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private const uint ABM_NEW = 0x0, ABM_REMOVE = 0x1;
    private const int ABN_FULLSCREENAPP = 0x2;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public NativeMethods.RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const int OBJID_WINDOW = 0;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int WS_CAPTION = 0x00C00000;
    private const long WS_EX_TRANSPARENT = 0x00000020;

    private readonly HwndSource _source;
    private readonly uint _callbackMessage;
    private readonly uint _taskbarCreated;
    private readonly WinEventProc _foregroundProc; // kept alive: the hook holds a raw pointer to it
    private readonly IntPtr _foregroundHook;
    private readonly WinEventProc _locationProc;
    private IntPtr _locationHook;   // resize/move of the current foreground window only (F11 in place)
    private IntPtr _foregroundWindow;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private bool _registered;

    public FullscreenWatcher()
    {
        _callbackMessage = RegisterWindowMessage("Knob.AppBarNotify");
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        // A real (hidden, zero-size) top-level window: message-only windows don't get appbar notifications.
        _source = new HwndSource(new HwndSourceParameters("KnobFullscreenWatcher")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
        });
        _source.AddHook(WndProc);
        Register();

        // Out-of-context hook: callbacks arrive on this (UI) thread's message loop.
        _foregroundProc = (_, _, _, _, _, _, _) => OnForegroundChanged();
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        _locationProc = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject == OBJID_WINDOW && hwnd == _foregroundWindow) Evaluate();
        };
        OnForegroundChanged();
    }

    /// <summary>New foreground window: follow its size changes (scoped to its process) and re-check.</summary>
    private void OnForegroundChanged()
    {
        _foregroundWindow = GetForegroundWindow();
        if (_locationHook != IntPtr.Zero)
        {
            UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
        if (_foregroundWindow != IntPtr.Zero)
        {
            GetWindowThreadProcessId(_foregroundWindow, out var pid);
            if (pid != 0 && pid != _ownProcessId)
                _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
                    _locationProc, pid, 0, WINEVENT_OUTOFCONTEXT);
        }
        Evaluate();
    }

    /// <summary>Re-check whether the foreground window is a fullscreen app on the primary monitor.</summary>
    private void Evaluate() => Set(ForegroundIsFullscreen());

    /// <summary>true = a fullscreen app is in front on the primary monitor; false = it left. Raised on the UI thread.</summary>
    public event Action<bool>? FullscreenChanged;

    public bool IsFullscreen { get; private set; }

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private void Register()
    {
        var data = NewData();
        data.uCallbackMessage = _callbackMessage;
        _registered = SHAppBarMessage(ABM_NEW, ref data) != UIntPtr.Zero;
    }

    private void Unregister()
    {
        if (!_registered) return;
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _source.Handle };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _callbackMessage && wParam.ToInt32() == ABN_FULLSCREENAPP)
        {
            // Treat it only as a hint (Windows sometimes reports the desktop as "fullscreen"): re-check ourselves.
            Evaluate();
            handled = true;
        }
        else if ((uint)msg == _taskbarCreated)
        {
            // Explorer restarted: our appbar registration is gone.
            _registered = false;
            Register();
            Evaluate();
        }
        return IntPtr.Zero;
    }

    private void Set(bool on)
    {
        if (IsFullscreen == on) return;
        IsFullscreen = on;
        FullscreenChanged?.Invoke(on);
    }

    /// <summary>
    /// Foreground window covers the whole primary monitor and is a real fullscreen app: not the desktop,
    /// not one of Knob's own windows (capture overlay), not an ordinary maximized window with a title bar
    /// (those cover the monitor when the taskbar auto-hides), not a click-through overlay.
    /// </summary>
    private bool ForegroundIsFullscreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        GetWindowThreadProcessId(fg, out var pid);
        if (pid == _ownProcessId) return false;

        var cls = new StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW") return false; // the desktop

        var style = NativeMethods.GetWindowStyle(fg);
        if (IsZoomed(fg) && (style & WS_CAPTION) == WS_CAPTION) return false; // plain maximized window

        // Click-through overlays (screen recorders, remote-control/automation overlays, some game overlays) cover
        // the whole screen but aren't content; a real fullscreen video or game is never click-through.
        if ((NativeMethods.GetExStyle(fg) & WS_EX_TRANSPARENT) != 0) return false;

        var primary = NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        if (MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTOPRIMARY) != primary) return false;

        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(primary, ref mi) || !NativeMethods.GetWindowRect(fg, out var r)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
            && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }
}
