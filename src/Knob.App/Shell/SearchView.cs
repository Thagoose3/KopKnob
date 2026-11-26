using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Knob.App.Tiles;

namespace Knob.App.Shell;

/// <summary>One palette result: what it says and what Enter does.</summary>
public sealed record PaletteItem(string Title, string? Subtitle, string Glyph, Action Run, string? Keywords = null);

/// <summary>
/// Quick search in the panel: type to find pages, pinned apps, actions and clipboard items, or a sum to calculate.
/// ↑/↓ choose, Enter runs, Esc closes the panel.
/// </summary>
public static class SearchView
{
    private const int MaxResults = 6;

    /// <param name="query">Results for the text typed (empty text → suggestions).</param>
    public static FrameworkElement Create(Func<string, IReadOnlyList<PaletteItem>> query)
    {
        var box = new TextBox { FontSize = 15, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center };
        var icon = new TextBlock { Text = Ui.IconSearch, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 15, Foreground = Ui.Subtle, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(Ui.WithHint(box, "Search pages, apps, clipboard, actions — or type 12*7"));

        var list = new StackPanel();
        var hint = Ui.Text("↑ ↓ to choose · Enter to run · Esc to close", 10, Ui.Dim);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.Margin = new Thickness(0, 4, 4, 0);

        IReadOnlyList<PaletteItem> results = [];
        var selected = 0;

        void Render()
        {
            list.Children.Clear();
            for (var i = 0; i < results.Count; i++) list.Children.Add(Row(results[i], i == selected, i));
            if (results.Count == 0)
                list.Children.Add(Ui.Text(box.Text.Trim().Length == 0 ? "" : "No matches", 12, Ui.Dim));
        }

        FrameworkElement Row(PaletteItem item, bool active, int index)
        {
            var glyph = new TextBlock { Text = item.Glyph, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 13, Width = 26, Foreground = active ? Ui.Fg : Ui.Subtle, VerticalAlignment = VerticalAlignment.Center };
            var title = Ui.Text(item.Title, 12.5, Ui.Fg, active ? FontWeights.SemiBold : FontWeights.Normal);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var row = new DockPanel();
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            if (item.Subtitle is not null)
            {
                var sub = Ui.Text(item.Subtitle, 10.5, Ui.Dim);
                sub.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(sub, Dock.Right);
                row.Children.Add(sub);
            }
            row.Children.Add(title);
            var b = new Border
            {
                Child = row,
                Padding = new Thickness(8, 4, 10, 4),
                Margin = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(8),
                Background = active ? Ui.Control : Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            b.MouseEnter += (_, _) => { if (selected != index) { selected = index; Render(); } };
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; item.Run(); };
            return b;
        }

        void Refresh()
        {
            results = query(box.Text).Take(MaxResults).ToList();
            selected = 0;
            Render();
        }

        box.TextChanged += (_, _) => Refresh();
        box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down when results.Count > 0:
                    selected = (selected + 1) % results.Count;
                    Render();
                    e.Handled = true;
                    break;
                case Key.Up when results.Count > 0:
                    selected = (selected - 1 + results.Count) % results.Count;
                    Render();
                    e.Handled = true;
                    break;
                case Key.Enter when results.Count > 0:
                    e.Handled = true;
                    results[selected].Run();
                    break;
            }
        };

        var root = new DockPanel { Margin = new Thickness(10, 2, 10, 0) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(hint);
        root.Children.Add(list);
        Pages.Smooth(root);

        root.Loaded += (_, _) => root.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => Keyboard.Focus(box));
        Refresh();
        return root;
    }
}
