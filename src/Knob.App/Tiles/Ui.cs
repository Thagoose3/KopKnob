using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Knob.App.Tiles;

/// <summary>Small helpers for building tile views in code.</summary>
internal static class Ui
{
    // Segoe MDL2 / Fluent icon code points
    public const string IconPlay = "";
    public const string IconPause = "";
    public const string IconAdd = "";
    public const string IconRemove = "";
    public const string IconReset = "";
    public const string IconClose = "";
    public const string IconLeft = "";
    public const string IconRight = "";

    // Tab bar
    public const string IconHome = "";
    public const string IconMusic = "";
    public const string IconTimer = "";
    public const string IconClipboard = "";
    public const string IconNotes = "";
    public const string IconTodo = "";
    public const string IconCalendar = "";
    public const string IconCalculator = "";
    public const string IconQr = "";
    public const string IconSystem = "";
    public const string IconCapture = "";

    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    public static Brush Accent => Res<Brush>("AccentBrush");
    public static Brush Fg => Res<Brush>("ForegroundBrush");
    public static Brush Subtle => Res<Brush>("SubtleForegroundBrush");
    public static Brush Dim => Res<Brush>("DimForegroundBrush");
    public static Brush Control => Res<Brush>("ControlBrush");

    public static TextBlock Text(string text, double size = 12, Brush? fg = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = fg ?? Fg,
        FontWeight = weight ?? FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Title(string text) => new() { Text = text, Style = Res<Style>("TileTitle") };

    public static Button IconButton(string glyph, RoutedEventHandler onClick, double width = 30, double height = 24, bool ghost = false)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Res<FontFamily>("IconFont"), FontSize = 11 },
            Width = width,
            Height = height,
            Margin = new Thickness(2, 0, 2, 0),
        };
        if (ghost) b.Style = Res<Style>("GhostButton");
        b.Click += onClick;
        return b;
    }

    /// <summary>Grey hint text shown over an empty TextBox.</summary>
    public static Grid WithHint(TextBox box, string hint)
    {
        var hintText = new TextBlock
        {
            Text = hint,
            Foreground = Dim,
            FontSize = box.FontSize,
            Margin = new Thickness(7, 3, 0, 0),
            IsHitTestVisible = false,
            VerticalAlignment = box.AcceptsReturn ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        void Sync() => hintText.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();
        var g = new Grid();
        g.Children.Add(box);
        g.Children.Add(hintText);
        return g;
    }

    /// <summary>Clockwise arc from 12 o'clock covering <paramref name="fraction"/> of a circle.</summary>
    public static Geometry Arc(double fraction, double size, double thickness)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var r = (size - thickness) / 2;
        var c = size / 2;
        if (fraction <= 0.0001) return Geometry.Empty;
        if (fraction >= 0.9999) return new EllipseGeometry(new Point(c, c), r, r);

        var a = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    /// <summary>Subscribe while the view is in the tree, so rebuilt views don't leak handlers.</summary>
    public static void WhileLoaded(FrameworkElement view, Action subscribe, Action unsubscribe)
    {
        view.Loaded += (_, _) => subscribe();
        view.Unloaded += (_, _) => unsubscribe();
    }
}
