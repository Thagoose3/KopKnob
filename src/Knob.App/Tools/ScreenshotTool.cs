using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Knob.App.Platform;
using Knob.App.Shell;
using Knob.App.Tiles;

namespace Knob.App.Tools;

/// <summary>
/// Hotkey → freeze the screen → drag a box (Enter = whole screen) → PNG in Pictures\Knob Screenshots, also put on
/// the clipboard and parked on the Shelf, so it can be dragged straight into a chat or converted.
/// </summary>
public sealed class ScreenshotTool(NotchWindow window, NotchController controller, ShelfTile shelf)
{
    private static readonly TimeSpan MessageTime = TimeSpan.FromSeconds(2.5);
    private bool _busy;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Knob Screenshots");

    public async void Start()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (controller.IsExpanded)
            {
                controller.RequestClose();
                await Task.Delay(220); // let the panel collapse before the screenshot
            }

            using var shot = ScreenCapture.CaptureVirtualScreen(out var bounds);
            var overlay = new CaptureOverlayWindow(ScreenCapture.ToBitmapSource(shot), bounds, "Knob screenshot", allowWholeScreen: true);
            var picked = await overlay.PickAsync();
            if (picked is not { } r) return;

            string path;
            using (var crop = shot.Clone(new Rectangle(r.X, r.Y, r.Width, r.Height), PixelFormat.Format32bppArgb))
            {
                Directory.CreateDirectory(Folder);
                path = UniquePath(Folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HH.mm.ss}");
                crop.Save(path, ImageFormat.Png);
                await ClipboardUtil.TrySetAsync(ToDataObject(crop));
            }
            shelf.Add([path]);
            window.ShowPillMessage("\U0001F4F8 Screenshot on the Shelf · copied", MessageTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            window.ShowPillMessage("Screenshot failed", MessageTime);
        }
        finally
        {
            _busy = false;
        }
    }

    private static System.Windows.DataObject ToDataObject(Bitmap bmp)
    {
        var data = new System.Windows.DataObject();
        data.SetImage(ScreenCapture.ToBitmapSource(bmp));
        return data;
    }

    private static string UniquePath(string folder, string stem)
    {
        var path = Path.Combine(folder, stem + ".png");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}).png");
        return path;
    }
}
