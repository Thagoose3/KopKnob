using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Knob.App.Tools;

public enum QrStyle { Square, Dots, Rounded }

/// <summary>
/// QRCoder builds the module matrix (ECC M, 4-module quiet zone); we draw it ourselves for the styles.
/// The three finder patterns are always drawn as solid squares so every style still scans.
/// </summary>
public static class QrRenderer
{
    private const int QuietZone = 4;

    /// <returns>Dark-module matrix including the quiet zone, or null with an error.</returns>
    public static bool[,]? BuildMatrix(string text, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(text))
        {
            error = "Type text to encode";
            return null;
        }
        try
        {
            using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
            var rows = data.ModuleMatrix;
            var n = rows.Count;
            var m = new bool[n, n];
            for (var r = 0; r < n; r++)
                for (var c = 0; c < n; c++)
                    m[r, c] = rows[r][c];
            return m;
        }
        catch (QRCoder.Exceptions.DataTooLongException)
        {
            error = "Too long for a QR code";
            return null;
        }
        catch (Exception)
        {
            error = "Couldn't make a QR code";
            return null;
        }
    }

    public static BitmapSource Render(bool[,] m, int pixels, QrStyle style, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixels, pixels));
            var n = m.GetLength(0);
            var cell = (double)pixels / n;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (var r = 0; r < n; r++)
                    for (var c = 0; c < n; c++)
                    {
                        if (!m[r, c]) continue;
                        var x = c * cell;
                        var y = r * cell;
                        if (style == QrStyle.Square || IsFinder(r, c, n))
                            AddRect(g, x, y, cell, cell, 0);
                        else if (style == QrStyle.Dots)
                            AddEllipse(g, x + cell / 2, y + cell / 2, cell * 0.45);
                        else
                            AddRect(g, x + cell * 0.04, y + cell * 0.04, cell * 0.92, cell * 0.92, cell * 0.3);
                    }
            }
            geo.Freeze();
            dc.DrawGeometry(brush, null, geo);
        }
        var bmp = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Inside one of the three 7×7 finder patterns (top-left, top-right, bottom-left).</summary>
    private static bool IsFinder(int r, int c, int n)
    {
        bool In(int v, int start) => v >= start && v < start + 7;
        var far = n - QuietZone - 7;
        return (In(r, QuietZone) && In(c, QuietZone))
            || (In(r, QuietZone) && In(c, far))
            || (In(r, far) && In(c, QuietZone));
    }

    private static void AddRect(StreamGeometryContext g, double x, double y, double w, double h, double radius)
    {
        if (radius <= 0)
        {
            // +0.5 px overlap so adjacent squares don't show hairline seams.
            g.BeginFigure(new Point(x, y), true, true);
            g.PolyLineTo([new Point(x + w + 0.5, y), new Point(x + w + 0.5, y + h + 0.5), new Point(x, y + h + 0.5)], false, false);
            return;
        }
        var rr = Math.Min(radius, Math.Min(w, h) / 2);
        var size = new Size(rr, rr);
        g.BeginFigure(new Point(x + rr, y), true, true);
        g.LineTo(new Point(x + w - rr, y), false, false);
        g.ArcTo(new Point(x + w, y + rr), size, 0, false, SweepDirection.Clockwise, false, false);
        g.LineTo(new Point(x + w, y + h - rr), false, false);
        g.ArcTo(new Point(x + w - rr, y + h), size, 0, false, SweepDirection.Clockwise, false, false);
        g.LineTo(new Point(x + rr, y + h), false, false);
        g.ArcTo(new Point(x, y + h - rr), size, 0, false, SweepDirection.Clockwise, false, false);
        g.LineTo(new Point(x, y + rr), false, false);
        g.ArcTo(new Point(x + rr, y), size, 0, false, SweepDirection.Clockwise, false, false);
    }

    private static void AddEllipse(StreamGeometryContext g, double cx, double cy, double r)
    {
        g.BeginFigure(new Point(cx - r, cy), true, true);
        g.ArcTo(new Point(cx + r, cy), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
        g.ArcTo(new Point(cx - r, cy), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
    }
}
