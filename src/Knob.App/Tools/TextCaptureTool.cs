using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Knob.App.Platform;
using Knob.App.Shell;
using Knob.Core.Ocr;

namespace Knob.App.Tools;

/// <summary>
/// Hotkey → freeze screen → drag a box → OCR (Thai + English) → clipboard.
/// Progress and result are shown on the pill.
/// </summary>
public sealed class TextCaptureTool(NotchWindow window, NotchController controller, IOcrService ocr)
{
    private static readonly TimeSpan MessageTime = TimeSpan.FromSeconds(2);
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
                await Task.Delay(220); // let the panel collapse before the screenshot
            }

            using var shot = ScreenCapture.CaptureVirtualScreen(out var bounds);
            var overlay = new CaptureOverlayWindow(ScreenCapture.ToBitmapSource(shot), bounds);
            var picked = await overlay.PickAsync();
            if (picked is not { } r) return;

            byte[] png;
            using (var crop = shot.Clone(new Rectangle(r.X, r.Y, r.Width, r.Height), PixelFormat.Format32bppArgb))
                png = Prepare(crop);

            window.ShowPillMessage("Reading…");
            string text;
            try
            {
                text = ThaiTextCleaner.Clean(await ocr.RecognizeAsync(png));
            }
            catch (OcrDataMissingException)
            {
                window.ShowPillMessage("OCR data missing", MessageTime);
                return;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException or InvalidOperationException or SEHException)
            {
                window.ShowPillMessage("OCR failed", MessageTime);
                return;
            }

            if (text.Length == 0)
            {
                window.ShowPillMessage("No text found", MessageTime);
                return;
            }
            var ok = await ClipboardUtil.TrySetTextAsync(text);
            window.ShowPillMessage(ok ? $"Copied {text.Length} chars" : "Clipboard busy", MessageTime);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Grayscale, upscale (×3 for short selections, else ×2), invert dark backgrounds → PNG.</summary>
    internal static byte[] Prepare(Bitmap crop)
    {
        var invert = OcrPreprocess.ShouldInvert(MeanLuma(crop));
        var scale = OcrPreprocess.ScaleFactor(crop.Height);

        // Luma weights per channel; inverted = 1 - luma.
        float a = invert ? -1f : 1f;
        float o = invert ? 1f : 0f;
        var gray = new ColorMatrix(
        [
            [0.299f * a, 0.299f * a, 0.299f * a, 0, 0],
            [0.587f * a, 0.587f * a, 0.587f * a, 0, 0],
            [0.114f * a, 0.114f * a, 0.114f * a, 0, 0],
            [0, 0, 0, 1, 0],
            [o, o, o, 0, 1],
        ]);

        using var output = new Bitmap(crop.Width * scale, crop.Height * scale, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(output))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(gray);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(crop, new Rectangle(0, 0, output.Width, output.Height), 0, 0, crop.Width, crop.Height, GraphicsUnit.Pixel, attrs);
        }
        using var ms = new MemoryStream();
        output.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static double MeanLuma(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            double sum = 0;
            long count = 0;
            for (var y = 0; y < data.Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < data.Width; x++)
                {
                    var i = row + x * 4; // BGRA
                    sum += 0.114 * bytes[i] + 0.587 * bytes[i + 1] + 0.299 * bytes[i + 2];
                    count++;
                }
            }
            return count == 0 ? 255 : sum / count;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
