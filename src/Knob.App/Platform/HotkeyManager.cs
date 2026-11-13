using System.Runtime.InteropServices;
using System.Windows.Interop;
using Knob.Core.Input;

namespace Knob.App.Platform;

/// <summary>Global hotkeys via RegisterHotKey on a message-only window. Unregistered on Dispose (quit).</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("KnobHotkeys") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <returns>false if another app already owns this combination.</returns>
    public bool Register(Hotkey hotkey, Action onPressed)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.VirtualKey))
            return false;
        _handlers[id] = onPressed;
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
