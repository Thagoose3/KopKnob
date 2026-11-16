using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Knob.App.Platform;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>Small wrappers that turn a tile view into a full panel page.</summary>
internal static class Pages
{
    /// <summary>Fill the page, with a little breathing room.</summary>
    public static FrameworkElement Padded(FrameworkElement view) =>
        new Border { Padding = new Thickness(10, 4, 10, 2), Child = view };

    /// <summary>Centre a compact view and scale it up (fonts, ring, buttons) so it fills a page nicely.</summary>
    public static FrameworkElement Centered(FrameworkElement view, double scale, double width = double.NaN, double height = double.NaN)
    {
        view.Width = width;
        view.Height = height;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
        view.LayoutTransform = new ScaleTransform(scale, scale);
        return new Grid { Children = { view } };
    }

    /// <summary>A rounded card in the tile colour.</summary>
    public static Border Card(UIElement child, Thickness? margin = null) => new()
    {
        Background = Ui.Res<Brush>("TileBrush"),
        CornerRadius = Ui.Res<CornerRadius>("TileCornerRadius"),
        Padding = new Thickness(12, 8, 12, 8),
        Margin = margin ?? new Thickness(4),
        Child = child,
    };
}

/// <summary>Home page: big clock and date on the left; Coffee, disk space and text capture on the right.</summary>
internal static class HomePage
{
    public static FrameworkElement Create(ClockTile clock, CoffeeTile coffee, DiskTile disk, Action captureText)
    {
        var bigClock = new Viewbox
        {
            Child = clock.CreateView(TileSize.W),
            Stretch = Stretch.Uniform,
            MaxHeight = 140,
            Margin = new Thickness(12, 0, 12, 0),
        };

        var capture = new Button { Height = 40, Margin = new Thickness(4), HorizontalContentAlignment = HorizontalAlignment.Left };
        var captureContent = new StackPanel { Orientation = Orientation.Horizontal };
        captureContent.Children.Add(new TextBlock { Text = Ui.IconCapture, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        captureContent.Children.Add(Ui.Text("Capture text", 12));
        ((TextBlock)captureContent.Children[1]).Margin = new Thickness(8, 0, 0, 0);
        capture.Content = captureContent;
        capture.ToolTip = "Drag a box on screen; the text goes to the clipboard";
        capture.Click += (_, _) => captureText();

        var coffeeView = coffee.CreateView(TileSize.W);
        coffeeView.Height = 44;
        coffeeView.Margin = new Thickness(4);

        var right = new StackPanel { Width = 230, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(coffeeView);
        right.Children.Add(Pages.Card(disk.CreateView(TileSize.W)));
        right.Children.Add(capture);

        var root = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);
        root.Children.Add(bigClock);
        return root;
    }
}
