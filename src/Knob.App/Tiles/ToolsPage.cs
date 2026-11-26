using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Knob.App.Shell;
using Knob.Core.Storage;

namespace Knob.App.Tiles;

/// <summary>
/// The Tools tab: the quick search box on top; below it (while nothing is typed) the screen tools as cards —
/// Screenshot, Pick a colour, Capture text — with their hotkeys, and the colours picked recently.
/// </summary>
public static class ToolsPage
{
    public sealed record Tool(string Glyph, string Title, string Hotkey, Action Run);

    /// <param name="recentColorsChanged">Subscribe/unsubscribe to "a colour was picked".</param>
    public static FrameworkElement Create(
        Func<string, IReadOnlyList<PaletteItem>> palette,
        IReadOnlyList<Tool> tools,
        ToolSettings settings,
        Action<string> copyColor,
        Action<Action> subscribeColors,
        Action<Action> unsubscribeColors)
    {
        var cards = new UniformGrid { Columns = tools.Count, Margin = new Thickness(-4, 0, -4, 0) };
        foreach (var t in tools)
        {
            var (card, _, _, sub) = Pages.ActionCard(t.Glyph, t.Title, t.Run);
            sub.Text = t.Hotkey;
            card.ToolTip = $"{t.Title} ({t.Hotkey})";
            cards.Children.Add(card);
        }

        var colorsTitle = Ui.Text("Recent colours", 11, Ui.Subtle, FontWeights.SemiBold);
        colorsTitle.Margin = new Thickness(2, 10, 0, 6);
        var swatches = new WrapPanel();
        var colors = new StackPanel();
        colors.Children.Add(colorsTitle);
        colors.Children.Add(swatches);

        void RefreshColors()
        {
            swatches.Children.Clear();
            if (settings.RecentColors.Count == 0)
            {
                swatches.Children.Add(Ui.Text("Colours you pick from the screen show up here", 11, Ui.Dim));
                return;
            }
            foreach (var hex in settings.RecentColors) swatches.Children.Add(Swatch(hex, copyColor));
        }

        var empty = new StackPanel();
        empty.Children.Add(cards);
        empty.Children.Add(colors);
        Ui.WhileLoaded(empty, () => { subscribeColors(RefreshColors); RefreshColors(); }, () => unsubscribeColors(RefreshColors));
        RefreshColors();

        return SearchView.Create(palette, whenEmpty: empty, autoFocus: false);
    }

    private static FrameworkElement Swatch(string hex, Action<string> copy)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return new Border(); }

        var chip = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)), BorderThickness = new Thickness(1),
        };
        var label = Ui.Text(hex, 11, Ui.Fg);
        label.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        label.Margin = new Thickness(7, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(chip);
        row.Children.Add(label);

        var b = new Border
        {
            Child = row,
            Padding = new Thickness(6, 4, 9, 4),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(8),
            Background = Ui.Res<Brush>("TileBrush"),
            Cursor = Cursors.Hand,
            ToolTip = "Click to copy",
        };
        b.MouseEnter += (_, _) => b.Background = Ui.Control;
        b.MouseLeave += (_, _) => b.Background = Ui.Res<Brush>("TileBrush");
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; copy(hex); };
        return b;
    }
}
