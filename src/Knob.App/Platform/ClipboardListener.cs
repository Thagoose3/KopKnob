using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Knob.App.Platform;

/// <summary>WM_CLIPBOARDUPDATE on a message-only window. Event-driven, no polling.</summary>
public sealed class ClipboardListener : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;

    public ClipboardListener()
    {
        _source = new HwndSource(new HwndSourceParameters("KnobClipboardListener")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);
    }

    /// <summary>Raised on the UI thread.</summary>
    public event Action? Updated;

    public void Dispose()
    {
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            Updated?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
