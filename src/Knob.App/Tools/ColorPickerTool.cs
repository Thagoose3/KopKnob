using Knob.App.Platform;
using Knob.App.Shell;

namespace Knob.App.Tools;

/// <summary>Hotkey → freeze the screen → magnifier → click a pixel → "#RRGGBB" on the clipboard.</summary>
public sealed class ColorPickerTool(NotchWindow window, NotchController controller)
{
    private bool _busy;

    public async void Start()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (controller.IsExpanded)
            {
                controller.RequestClose();
                await Task.Delay(220); // let the panel collapse so its own colours aren't picked
            }

            using var shot = ScreenCapture.CaptureVirtualScreen(out var bounds);
            var picker = new ColorPickerWindow(shot, ScreenCapture.ToBitmapSource(shot), bounds);
            if (await picker.PickAsync() is not string hex) return;

            var ok = await ClipboardUtil.TrySetTextAsync(hex);
            window.ShowPillMessage(ok ? $"\U0001F3A8 {hex} copied" : "Clipboard busy", TimeSpan.FromSeconds(2.5));
        }
        finally
        {
            _busy = false;
        }
    }
}
