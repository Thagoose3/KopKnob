using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Knob.App.Platform;

/// <summary>
/// Notices input-language switches (Grave/`~`, Alt+Shift, Ctrl+Shift, Win+Space) and Caps Lock.
/// A low-level keyboard hook looks only at the virtual-key code of key-ups of those keys — it never records
/// what is typed — and asks for a check of the foreground window's keyboard layout shortly after.
/// </summary>
public sealed class KeyboardWatcher : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    private const int VK_CAPITAL = 0x14, VK_SPACE = 0x20, VK_OEM_3 = 0xC0;
    private const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const int VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private readonly Dispatcher _dispatcher;
    private readonly HookProc _proc; // keep the delegate alive
    private readonly DispatcherTimer _layoutCheck;
    private readonly DispatcherTimer _capsCheck;
    private IntPtr _hook;
    private IntPtr _lastLayout;

    public KeyboardWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookCallback;
        // The switch is applied asynchronously by the target thread: look a moment later.
        _layoutCheck = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => CheckLayout(), dispatcher);
        _capsCheck = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Normal, (_, _) =>
        {
            _capsCheck!.Stop();
            CapsLockChanged?.Invoke((GetKeyState(VK_CAPITAL) & 1) != 0);
        }, dispatcher);
        _layoutCheck.Stop();
        _capsCheck.Stop();
    }

    /// <summary>Raised with the new layout's language id (LANGID) when the foreground input language changed.</summary>
    public event Action<int>? LanguageChanged;
    /// <summary>Raised with the new Caps Lock state.</summary>
    public event Action<bool>? CapsLockChanged;

    public bool Running => _hook != IntPtr.Zero;

    public void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        DiagLog.Write("Keyboard watcher: off");
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _lastLayout = ForegroundLayout();
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        DiagLog.Write(_hook == IntPtr.Zero
            ? $"Keyboard watcher: hook failed ({Marshal.GetLastWin32Error()})"
            : $"Keyboard watcher: on (layout 0x{_lastLayout.ToInt64() & 0xFFFF:X4})");
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == WM_KEYUP || wParam == WM_SYSKEYUP))
        {
            var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode — nothing else is read
            if (vk == VK_CAPITAL)
            {
                _dispatcher.BeginInvoke(() => { _capsCheck.Stop(); _capsCheck.Start(); });
            }
            else if (vk == VK_OEM_3
                     || (vk == VK_SPACE && (Down(VK_LWIN) || Down(VK_RWIN)))
                     || ((vk is VK_LSHIFT or VK_RSHIFT) && (Down(VK_LMENU) || Down(VK_RMENU) || Down(VK_LCONTROL) || Down(VK_RCONTROL)))
                     || ((vk is VK_LMENU or VK_RMENU or VK_LCONTROL or VK_RCONTROL) && Down(VK_SHIFT)))
            {
                _dispatcher.BeginInvoke(() => { _layoutCheck.Stop(); _layoutCheck.Start(); });
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void CheckLayout()
    {
        _layoutCheck.Stop();
        var layout = ForegroundLayout();
        if (layout == IntPtr.Zero || layout == _lastLayout) return;
        _lastLayout = layout;
        LanguageChanged?.Invoke((int)(layout.ToInt64() & 0xFFFF));
    }

    private static IntPtr ForegroundLayout()
    {
        var fg = GetForegroundWindow();
        var thread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        return GetKeyboardLayout(thread);
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _layoutCheck.Stop();
        _capsCheck.Stop();
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
}
