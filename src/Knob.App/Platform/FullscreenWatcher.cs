using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace Knob.App.Platform;

/// <summary>
/// Tells when an app goes fullscreen on the primary monitor, without polling: registers a hidden appbar
/// (ABM_NEW only — it reserves no screen space) and listens for ABN_FULLSCREENAPP.
/// Re-registers when Explorer restarts (TaskbarCreated). Unregisters on Dispose.
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

    private readonly HwndSource _source;
    private readonly uint _callbackMessage;
    private readonly uint _taskbarCreated;
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
    }

    /// <summary>true = a fullscreen app is in front on the primary monitor; false = it left. Raised on the UI thread.</summary>
    public event Action<bool>? FullscreenChanged;

    public bool IsFullscreen { get; private set; }

    public void Dispose()
    {
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
            // Known quirk: Windows sometimes reports the desktop itself as "fullscreen". Double-check.
            var on = lParam != IntPtr.Zero && ForegroundCoversPrimaryMonitor();
            Set(on);
            handled = true;
        }
        else if ((uint)msg == _taskbarCreated)
        {
            // Explorer restarted: our appbar registration is gone.
            _registered = false;
            Register();
            Set(false);
        }
        return IntPtr.Zero;
    }

    private void Set(bool on)
    {
        if (IsFullscreen == on) return;
        IsFullscreen = on;
        FullscreenChanged?.Invoke(on);
    }

    private static bool ForegroundCoversPrimaryMonitor()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var cls = new StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW") return false; // the desktop

        var primary = NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        if (MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTOPRIMARY) != primary) return false;

        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(primary, ref mi) || !NativeMethods.GetWindowRect(fg, out var r)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
            && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }
}
