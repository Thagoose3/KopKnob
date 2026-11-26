using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Knob.App.Platform;

namespace Knob.App.Tools;

/// <summary>
/// Frozen, dimmed screenshot over the whole virtual screen; drag a rectangle to pick text.
/// Esc or right-click cancels. The picked rectangle is mapped to screenshot pixels by ratio
/// (image is stretched to the window), so it stays correct at any DPI scaling.
/// </summary>
public sealed class CaptureOverlayWindow : Window
{
    private readonly BitmapSource _shot;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly Path _dim;
    private readonly Rectangle _frame;
    private readonly TaskCompletionSource<Int32Rect?> _result = new();
    private Point? _start;

    /// <param name="allowWholeScreen">Enter picks the whole screen (screenshots).</param>
    public CaptureOverlayWindow(BitmapSource screenshot, System.Drawing.Rectangle physicalBounds, string title = "Knob text capture", bool allowWholeScreen = false)
    {
        _shot = screenshot;
        _bounds = physicalBounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Title = title;
        if (allowWholeScreen)
            KeyDown += (_, e) => { if (e.Key == Key.Enter) Finish(new Int32Rect(0, 0, _shot.PixelWidth, _shot.PixelHeight)); };

        var image = new Image { Source = screenshot, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), IsHitTestVisible = false };
        _frame = new Rectangle
        {
            Stroke = (Brush)Application.Current.FindResource("AccentBrush"),
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var root = new Grid();
        root.Children.Add(image);
        root.Children.Add(_dim);
        root.Children.Add(_frame);
        Content = root;

        Loaded += (_, _) => { UpdateDim(null); Activate(); Focus(); };
        SizeChanged += (_, _) => UpdateDim(null);
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) => Finish(null);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        Deactivated += (_, _) => Finish(null);
    }

    /// <summary>Selected rectangle in screenshot pixels, or null if cancelled.</summary>
    public Task<Int32Rect?> PickAsync()
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

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_start is not Point s) return;
        UpdateDim(new Rect(s, e.GetPosition(this)));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is not Point s) return;
        ReleaseMouseCapture();
        var sel = new Rect(s, e.GetPosition(this));
        _start = null;
        if (sel.Width < 3 || sel.Height < 3) { UpdateDim(null); return; } // a click, not a drag

        var sx = _shot.PixelWidth / ActualWidth;
        var sy = _shot.PixelHeight / ActualHeight;
        var x = (int)Math.Floor(sel.X * sx);
        var y = (int)Math.Floor(sel.Y * sy);
        var w = (int)Math.Ceiling(sel.Width * sx);
        var h = (int)Math.Ceiling(sel.Height * sy);
        x = Math.Clamp(x, 0, _shot.PixelWidth - 1);
        y = Math.Clamp(y, 0, _shot.PixelHeight - 1);
        w = Math.Clamp(w, 1, _shot.PixelWidth - x);
        h = Math.Clamp(h, 1, _shot.PixelHeight - y);
        Finish(new Int32Rect(x, y, w, h));
    }

    private void UpdateDim(Rect? sel)
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (sel is Rect r && r.Width > 0 && r.Height > 0)
        {
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(r));
            _frame.Margin = new Thickness(r.X, r.Y, 0, 0);
            _frame.Width = r.Width;
            _frame.Height = r.Height;
            _frame.Visibility = Visibility.Visible;
        }
        else
        {
            _dim.Data = full;
            _frame.Visibility = Visibility.Collapsed;
        }
    }

    private void Finish(Int32Rect? result)
    {
        if (_result.Task.IsCompleted) return;
        _result.TrySetResult(result);
        Close();
    }
}
