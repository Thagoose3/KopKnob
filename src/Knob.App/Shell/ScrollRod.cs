using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Knob.App.Shell;

/// <summary>
/// One roller of the sutra scroll: red lacquer shaft with gold bands, gold collars and jade-green pointed finials
/// that stick out past the paper at both ends. Built vertically; rotated for horizontal use.
/// </summary>
internal static class ScrollRod
{
    public const double Width = 16;
    public const double FinialLength = 9;
    public const double CollarLength = 3;
    /// <summary>How far the rod extends past the scroll body at each end.</summary>
    public const double Overhang = FinialLength + CollarLength;

    private static readonly Brush Shaft = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0x5E, 0x10, 0x0C), 0),
        new(Color.FromRgb(0xC9, 0x30, 0x26), 0.38),
        new(Color.FromRgb(0xFF, 0x7A, 0x62), 0.52),
        new(Color.FromRgb(0x9A, 0x1C, 0x15), 0.75),
        new(Color.FromRgb(0x4E, 0x0C, 0x09), 1),
    }, new Point(0, 0), new Point(1, 0)));

    private static readonly Brush Gold = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0x7A, 0x4A, 0x10), 0),
        new(Color.FromRgb(0xF7, 0xD0, 0x4A), 0.4),
        new(Color.FromRgb(0xFF, 0xF1, 0xB0), 0.55),
        new(Color.FromRgb(0x8A, 0x5A, 0x10), 1),
    }, new Point(0, 0), new Point(1, 0)));

    private static readonly Brush Jade = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0x12, 0x4A, 0x37), 0),
        new(Color.FromRgb(0x5F, 0xC7, 0x9C), 0.45),
        new(Color.FromRgb(0xB8, 0xF0, 0xD6), 0.55),
        new(Color.FromRgb(0x1E, 0x6B, 0x50), 1),
    }, new Point(0, 0), new Point(1, 0)));

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    public static FrameworkElement Create(bool vertical)
    {
        var grid = new Grid { Width = Width, IsHitTestVisible = false };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FinialLength) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CollarLength) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CollarLength) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FinialLength) });

        Add(grid, Finial(flip: false), 0);
        Add(grid, new Border { Background = Gold, CornerRadius = new CornerRadius(1.5) }, 1);
        Add(grid, Shaft12(), 2);
        Add(grid, new Border { Background = Gold, CornerRadius = new CornerRadius(1.5) }, 3);
        Add(grid, Finial(flip: true), 4);

        if (!vertical) grid.LayoutTransform = new RotateTransform(-90);
        return grid;
    }

    private static void Add(Grid g, UIElement e, int row)
    {
        Grid.SetRow(e, row);
        g.Children.Add(e);
    }

    /// <summary>Red shaft (a bit narrower than the collars) with gold and jade bands near both ends.</summary>
    private static FrameworkElement Shaft12()
    {
        var shaft = new Grid { Width = Width - 4, HorizontalAlignment = HorizontalAlignment.Center };
        shaft.Children.Add(new Border { Background = Shaft });
        foreach (var top in new[] { true, false })
        {
            var va = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            shaft.Children.Add(new Border { Background = Gold, Height = 3, VerticalAlignment = va, Margin = top ? new Thickness(0, 5, 0, 0) : new Thickness(0, 0, 0, 5) });
            shaft.Children.Add(new Border { Background = Jade, Height = 2, VerticalAlignment = va, Margin = top ? new Thickness(0, 10, 0, 0) : new Thickness(0, 0, 0, 10) });
        }
        return shaft;
    }

    /// <summary>Jade finial: a pointed dome on a small gold bead.</summary>
    private static FrameworkElement Finial(bool flip)
    {
        var dome = new Path
        {
            Data = Geometry.Parse("M8,0 C10.5,2.5 13,5 12.5,9 L3.5,9 C3,5 5.5,2.5 8,0 Z"),
            Fill = Jade,
            Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x0E, 0x3A, 0x2B)),
            StrokeThickness = 0.6,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var holder = new Grid { Children = { dome } };
        if (flip) holder.LayoutTransform = new ScaleTransform(1, -1);
        return holder;
    }
}
