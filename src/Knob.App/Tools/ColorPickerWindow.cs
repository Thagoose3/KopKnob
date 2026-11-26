using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Knob.App.Platform;
using Knob.Core.Color;

namespace Knob.App.Tools;

/// <summary>
/// Frozen screenshot over the whole virtual screen with a magnifier that follows the pointer (11×11 pixels at 12×)
/// and the colour under its centre. Click picks it; Esc, right-click or switching away cancels.
/// </summary>
public sealed class ColorPickerWindow : Window
{
    private const int Span = 11;   // pixels shown across the loupe (odd, so there is a centre pixel)
    private const double Zoom = 12;

    private readonly System.Drawing.Bitmap _pixels;
    private readonly BitmapSource _shot;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly Canvas _canvas = new();
    private readonly Border _loupe;
    private readonly ImageBrush _zoom;
    private readonly Border _swatch;
    private readonly TextBlock _hex;
    private readonly TaskCompletionSource<string?> _result = new();
    private (byte R, byte G, byte B) _color;

    public ColorPickerWindow(System.Drawing.Bitmap pixels, BitmapSource screenshot, System.Drawing.Rectangle physicalBounds)
    {
        _pixels = pixels;
        _shot = screenshot;
        _bounds = physicalBounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Title = "Knob colour picker";

        var image = new Image { Source = screenshot, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        _zoom = new ImageBrush(screenshot) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
        var size = Span * Zoom;
        var magnified = new Grid { Width = size, Height = size };
        magnified.Children.Add(new Rectangle { Fill = _zoom });
        magnified.Children.Add(new Rectangle // the centre pixel
        {
            Width = Zoom + 2, Height = Zoom + 2, Stroke = Brushes.White, StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });

        _swatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1) };
        _hex = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6) };
        label.Children.Add(_swatch);
        label.Children.Add(_hex);

        var stack = new StackPanel();
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(10, 10, 0, 0), ClipToBounds = true, Child = magnified });
        stack.Children.Add(label);
        _loupe = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x16, 0x1C)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = stack,
            IsHitTestVisible = false,
            Visibility = Visibility.Hidden,
        };
        _canvas.Children.Add(_loupe);

        var root = new Grid();
        root.Children.Add(image);
        root.Children.Add(_canvas);
        Content = root;

        Loaded += (_, _) => { Activate(); Focus(); };
        MouseMove += (_, e) => Track(e.GetPosition(this));
        MouseLeftButtonUp += (_, _) => Finish(ColorText.Hex(_color.R, _color.G, _color.B));
        MouseRightButtonUp += (_, _) => Finish(null);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        Deactivated += (_, _) => Finish(null);
    }

    /// <summary>"#RRGGBB" of the picked pixel, or null if cancelled.</summary>
    public Task<string?> PickAsync()
    {
        Show();
        return _result.Task;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, 0);
    }

    private void Track(Point p)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var px = Math.Clamp((int)(p.X * _shot.PixelWidth / ActualWidth), 0, _shot.PixelWidth - 1);
        var py = Math.Clamp((int)(p.Y * _shot.PixelHeight / ActualHeight), 0, _shot.PixelHeight - 1);

        var c = _pixels.GetPixel(px, py);
        _color = (c.R, c.G, c.B);
        _swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        _hex.Text = ColorText.Hex(c.R, c.G, c.B);

        // The brush works in screenshot units (1 unit = 1 pixel at 96 dpi, as the capture is).
        var unitX = _shot.Width / _shot.PixelWidth;
        var unitY = _shot.Height / _shot.PixelHeight;
        _zoom.Viewbox = new Rect((px - Span / 2) * unitX, (py - Span / 2) * unitY, Span * unitX, Span * unitY);

        // Beside the pointer, flipped near the right/bottom edges.
        _loupe.Visibility = Visibility.Visible;
        _loupe.UpdateLayout();
        var x = p.X + 24;
        var y = p.Y + 24;
        if (x + _loupe.ActualWidth > ActualWidth) x = p.X - 24 - _loupe.ActualWidth;
        if (y + _loupe.ActualHeight > ActualHeight) y = p.Y - 24 - _loupe.ActualHeight;
        Canvas.SetLeft(_loupe, x);
        Canvas.SetTop(_loupe, y);
    }

    private void Finish(string? result)
    {
        if (_result.Task.IsCompleted) return;
        _result.TrySetResult(result);
        Close();
    }
}
