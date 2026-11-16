using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Knob.Core.Storage;

namespace Knob.App.Tiles;

/// <summary>Small wrappers that turn a tile view into a full panel page.</summary>
internal static class Pages
{
    /// <summary>Fill the page, with a little breathing room.</summary>
    public static FrameworkElement Padded(FrameworkElement view) =>
        new Border { Padding = new Thickness(10, 4, 10, 2), Child = view };

    /// <summary>
    /// Centre a compact view and scale it up (fonts, ring, buttons) so it fills a page nicely.
    /// Text uses "Ideal" formatting: the window's "Display" mode is tuned for small text and looks jagged when scaled.
    /// </summary>
    public static FrameworkElement Centered(FrameworkElement view, double scale, double width = double.NaN, double height = double.NaN)
    {
        view.Width = width;
        view.Height = height;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
        view.LayoutTransform = new ScaleTransform(scale, scale);
        var host = new Grid { Children = { view } };
        Smooth(host);
        return host;
    }

    /// <summary>Crisp rendering for large or scaled text.</summary>
    public static T Smooth<T>(T element) where T : FrameworkElement
    {
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(element, TextRenderingMode.Grayscale);
        return element;
    }

    /// <summary>A rounded card in the tile colour.</summary>
    public static Border Card(UIElement child, Thickness? margin = null) => new()
    {
        Background = Ui.Res<Brush>("TileBrush"),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14, 10, 14, 10),
        Margin = margin ?? new Thickness(4),
        Child = child,
    };

    /// <summary>
    /// Clickable card: round icon badge, title, subtitle, optional extra row underneath; lightens on hover.
    /// Returns the card plus the pieces callers update.
    /// </summary>
    public static (Border Card, Border Badge, TextBlock Title, TextBlock Subtitle) ActionCard(
        string glyph, string title, Action onClick, UIElement? extra = null)
    {
        var badgeIcon = new TextBlock
        {
            Text = glyph,
            FontFamily = Ui.Res<FontFamily>("IconFont"),
            FontSize = 15,
            Foreground = Ui.Fg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Ui.Control, Child = badgeIcon };
        var titleText = Ui.Text(title, 13, weight: FontWeights.SemiBold);
        var subtitle = Ui.Text("", 10.5, Ui.Subtle);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        texts.Children.Add(titleText);
        texts.Children.Add(subtitle);
        if (extra is not null) texts.Children.Add(extra);

        var row = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(texts);

        var card = Card(row);
        card.Cursor = Cursors.Hand;
        var normal = card.Background;
        card.MouseEnter += (_, _) => card.Background = Ui.Res<Brush>("ControlBrush");
        card.MouseLeave += (_, _) => card.Background = normal;
        card.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        return (card, badge, titleText, subtitle);
    }

    /// <summary>
    /// Theme-coloured slider (0–1): drag or click anywhere on it, or use the mouse wheel. <paramref name="changed"/>
    /// runs on every change made by the user; the returned setter updates it from code without raising it.
    /// </summary>
    public static (FrameworkElement Slider, Action<double> Set) Slider(Action<double> changed, double height = 8)
    {
        var value = 0.0;
        var (bar, setBar) = Bar(Ui.Accent, height);
        bar.Margin = new Thickness(0);
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = height + 8,
            Height = height + 8,
            Fill = Ui.Fg,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var root = new Grid { Height = height + 12, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        root.Children.Add(bar);
        root.Children.Add(knob);

        void Show()
        {
            setBar(value);
            var w = Math.Max(0, root.ActualWidth - knob.Width);
            knob.Margin = new Thickness(w * value, 0, 0, 0);
        }
        void SetFromUser(double v)
        {
            v = Math.Clamp(v, 0, 1);
            if (Math.Abs(v - value) < 0.0005) return;
            value = v;
            Show();
            changed(v);
        }
        double At(MouseEventArgs e) => root.ActualWidth <= 0 ? value : e.GetPosition(root).X / root.ActualWidth;

        root.SizeChanged += (_, _) => Show();
        root.MouseLeftButtonDown += (_, e) => { root.CaptureMouse(); SetFromUser(At(e)); e.Handled = true; };
        root.MouseMove += (_, e) => { if (root.IsMouseCaptured) SetFromUser(At(e)); };
        root.MouseLeftButtonUp += (_, e) => { root.ReleaseMouseCapture(); e.Handled = true; };
        root.MouseWheel += (_, e) => { SetFromUser(value + e.Delta / 120.0 * 0.02); e.Handled = true; };
        return (root, v => { value = Math.Clamp(v, 0, 1); Show(); });
    }

    /// <summary>On/off switch in the theme colours. <paramref name="changed"/> runs when the user flips it.</summary>
    public static (FrameworkElement Switch, Action<bool> Set) Toggle(bool on, Action<bool> changed)
    {
        var knob = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = Ui.Fg, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(3, 0, 3, 0), Child = knob, Cursor = Cursors.Hand };
        void Show()
        {
            track.Background = on ? Ui.Accent : Ui.Control;
            knob.Fill = on ? Ui.AccentFg : Ui.Subtle;
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        track.MouseLeftButtonUp += (_, e) =>
        {
            on = !on;
            Show();
            changed(on);
            e.Handled = true;
        };
        Show();
        return (track, v => { on = v; Show(); });
    }

    /// <summary>A row of buttons where one is selected (segmented control).</summary>
    public static FrameworkElement Choice<T>(IReadOnlyList<(string Label, T Value)> options, T selected, Action<T> picked)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<(Button Button, T Value)>();
        void Show()
        {
            foreach (var (b, v) in buttons)
            {
                var sel = EqualityComparer<T>.Default.Equals(v, selected);
                b.Background = sel ? Ui.Accent : Ui.Control;
                b.Foreground = sel ? Ui.AccentFg : Ui.Fg;
            }
        }
        foreach (var (label, value) in options)
        {
            var b = new Button { Content = label, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 4, 0), MinWidth = 40, Height = 24 };
            b.Click += (_, _) =>
            {
                if (EqualityComparer<T>.Default.Equals(value, selected)) return;
                selected = value;
                Show();
                picked(value);
            };
            buttons.Add((b, value));
            row.Children.Add(b);
        }
        Show();
        return row;
    }

    /// <summary>Thin rounded progress bar; returns the bar and a setter for 0–1.</summary>
    public static (Grid Bar, Action<double> Set) Bar(Brush fill, double height = 4)
    {
        var bar = new Grid { Height = height, Margin = new Thickness(0, 6, 0, 0) };
        var done = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        var rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        bar.ColumnDefinitions.Add(done);
        bar.ColumnDefinitions.Add(rest);
        var track = new Border { Background = Ui.Control, CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
        return (bar, f =>
        {
            f = Math.Clamp(f, 0, 1);
            done.Width = new GridLength(Math.Max(0.0001, f), GridUnitType.Star);
            rest.Width = new GridLength(Math.Max(0.0001, 1 - f), GridUnitType.Star);
        });
    }
}

/// <summary>Home page: a big crisp clock and date on the left; Coffee, the system drive and text capture as cards on the right.</summary>
internal static class HomePage
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static FrameworkElement Create(ClockTile clock, CoffeeTile coffee, DiskTile disk, string captureHotkey, Action captureText)
    {
        // Clock — real font sizes, no scaling, so it stays sharp.
        var time = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 76,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Fg,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var date = Ui.Text("", 15, Ui.Subtle);
        date.HorizontalAlignment = HorizontalAlignment.Center;
        date.Margin = new Thickness(0, -6, 0, 0);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(time);
        left.Children.Add(date);

        void RefreshClock()
        {
            var now = clock.Clock.Now;
            time.Text = now.ToString("HH:mm", English);
            date.Text = now.ToString("dddd d MMMM", English);
        }

        // Coffee
        var (coffeeCard, coffeeBadge, _, coffeeSub) = Pages.ActionCard("", "Coffee", () => coffee.Set(!coffee.IsOn));
        coffeeCard.ToolTip = "Keep the PC and screen awake";
        void RefreshCoffee()
        {
            coffeeSub.Text = coffee.IsOn ? "Keeping the PC awake" : "Off — PC can sleep";
            coffeeBadge.Background = coffee.IsOn ? Ui.Accent : Ui.Control;
            ((TextBlock)coffeeBadge.Child).Foreground = coffee.IsOn ? Ui.AccentFg : Ui.Fg;
        }

        // Disk — click to open the drive
        var (bar, setBar) = Pages.Bar(Ui.Accent);
        var (diskCard, _, diskTitle, diskSub) = Pages.ActionCard("", "Local Disk", disk.OpenInExplorer, bar);
        diskCard.ToolTip = "Open in File Explorer";
        void RefreshDisk()
        {
            diskTitle.Text = $"Local Disk ({disk.DriveName})";
            if (disk.TotalBytes <= 0)
            {
                diskSub.Text = "—";
                setBar(0);
                return;
            }
            diskSub.Text = $"{ByteFormatter.Format(disk.FreeBytes)} free of {ByteFormatter.Format(disk.TotalBytes)}";
            setBar(1 - disk.FreeBytes / (double)disk.TotalBytes);
        }

        // Capture text
        var (captureCard, _, _, captureSub) = Pages.ActionCard(Ui.IconCapture, "Capture text", captureText);
        captureSub.Text = $"{captureHotkey} · Thai + English";
        captureCard.ToolTip = "Drag a box on screen; the text goes to the clipboard";

        var right = new StackPanel { Width = 270, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(coffeeCard);
        right.Children.Add(diskCard);
        right.Children.Add(captureCard);

        var root = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);
        root.Children.Add(left);
        Pages.Smooth(root);

        Ui.WhileLoaded(root,
            () =>
            {
                clock.Refreshed += RefreshClock;
                coffee.Changed += RefreshCoffee;
                disk.Changed += RefreshDisk;
                RefreshClock();
                RefreshCoffee();
                RefreshDisk();
            },
            () =>
            {
                clock.Refreshed -= RefreshClock;
                coffee.Changed -= RefreshCoffee;
                disk.Changed -= RefreshDisk;
            });
        return root;
    }
}
