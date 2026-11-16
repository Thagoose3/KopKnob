using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Knob.App.Tiles;
using Knob.Core;
using Knob.Core.Shell;

namespace Knob.App.Shell;

/// <summary>One panel page: its tab icon/label, how to build its view, and the tiles to notify while it is on screen.</summary>
public sealed record PageDef(
    string Id,
    string Title,
    string Glyph,
    Func<FrameworkElement> Create,
    IReadOnlyList<ITile> Tiles,
    bool Recreate = false);

/// <summary>
/// The open panel: a header (date · ··· · time), one page at a time, and a bottom tab bar.
/// Only the page on screen gets OnPanelOpened/Closed, so e.g. the system monitor polls only while its tab is shown.
/// </summary>
public sealed class PageHost : DockPanel
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly Dictionary<string, PageDef> _pages;
    private readonly TabSettings _tabs;
    private readonly IClock _clock;
    private readonly Action _save;
    private readonly Dictionary<string, FrameworkElement> _views = [];
    private readonly Border _content = new();
    private readonly StackPanel _nav = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _date;
    private readonly TextBlock _time;
    private PageDef? _current;
    private bool _open;
    private bool _customizing;

    public PageHost(IEnumerable<PageDef> pages, TabSettings tabs, IClock clock, PillStatus pill, Action save)
    {
        _pages = pages.ToDictionary(p => p.Id);
        _tabs = tabs;
        _clock = clock;
        _save = save;

        // Header
        _date = Ui.Text("", 10, Ui.Subtle, FontWeights.SemiBold);
        _time = Ui.Text("", 10, Ui.Subtle, FontWeights.SemiBold);
        var more = Ui.IconButton("", (_, _) => ToggleCustomize(), 26, 18, ghost: true);
        more.ToolTip = "Customize tabs";
        ((TextBlock)more.Content).FontSize = 10;
        var header = new DockPanel { Margin = new Thickness(4, 0, 0, 6) };
        DockPanel.SetDock(more, Dock.Right);
        DockPanel.SetDock(_time, Dock.Right);
        _time.Margin = new Thickness(0, 0, 4, 0);
        header.Children.Add(more);
        header.Children.Add(_time);
        header.Children.Add(_date);
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);

        // Tab bar
        var navBar = new Border { Margin = new Thickness(0, 6, 0, 0), Child = _nav };
        DockPanel.SetDock(navBar, Dock.Bottom);
        Children.Add(navBar);

        Children.Add(_content);

        pill.MinuteTick += () => { if (_open) RefreshHeader(); };
        RebuildNav();
        Show(_tabs.Last ?? _tabs.Visible.FirstOrDefault() ?? _pages.Keys.First());
    }

    public string? CurrentId => _current?.Id;

    /// <summary>Show a page (also one hidden from the tab bar, e.g. opened by a hotkey).</summary>
    public void Show(string id)
    {
        if (!_pages.TryGetValue(id, out var page)) return;
        _customizing = false;
        if (_open && _current is not null) Notify(_current, opened: false);

        _current = page;
        if (page.Recreate || !_views.TryGetValue(id, out var view))
        {
            view = page.Create();
            if (!page.Recreate) _views[id] = view;
        }
        _content.Child = view;

        if (_open) Notify(page, opened: true);
        if (_tabs.Visible.Contains(id) && _tabs.Last != id)
        {
            _tabs.Last = id;
            _save();
        }
        RebuildNav();
    }

    public void ShowCustomize()
    {
        if (_open && _current is not null) Notify(_current, opened: false);
        _customizing = true;
        _content.Child = CustomizeView();
        RebuildNav();
    }

    private void ToggleCustomize()
    {
        if (_customizing) Show(_current?.Id ?? _tabs.Visible[0]);
        else ShowCustomize();
    }

    public void OnPanelOpened()
    {
        if (_open) return;
        _open = true;
        RefreshHeader();
        if (_current is not null && !_customizing) Notify(_current, opened: true);
    }

    public void OnPanelClosed()
    {
        if (!_open) return;
        if (_customizing) Show(_current?.Id ?? _tabs.Visible[0]); // leave customize when the panel closes
        if (_current is not null) Notify(_current, opened: false);
        _open = false;
    }

    private static void Notify(PageDef page, bool opened)
    {
        foreach (var t in page.Tiles)
        {
            try
            {
                if (opened) t.OnPanelOpened(); else t.OnPanelClosed();
            }
            catch (Exception)
            {
                // one tile must not break the panel
            }
        }
    }

    private void RefreshHeader()
    {
        var now = _clock.Now;
        _date.Text = now.ToString("ddd d MMM", English).ToUpperInvariant();
        _time.Text = now.ToString("HH:mm", English);
    }

    private void RebuildNav()
    {
        _nav.Children.Clear();
        foreach (var id in _tabs.Visible)
        {
            if (!_pages.TryGetValue(id, out var page)) continue;
            var active = !_customizing && _current?.Id == id;

            var icon = new TextBlock
            {
                Text = page.Glyph,
                FontFamily = Ui.Res<FontFamily>("IconFont"),
                FontSize = 13,
                Foreground = active ? Ui.Fg : Ui.Subtle,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            if (active)
            {
                content.Children.Add(new TextBlock
                {
                    Text = page.Title.ToUpperInvariant(),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Ui.Fg,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var button = new Button
            {
                Content = content,
                Height = 26,
                MinWidth = 32,
                Padding = new Thickness(active ? 10 : 8, 0, active ? 12 : 8, 0),
                Margin = new Thickness(2, 0, 2, 0),
                Background = active ? Ui.Control : Brushes.Transparent,
                ToolTip = page.Title,
            };
            var target = id;
            button.Click += (_, _) => Show(target);
            _nav.Children.Add(button);
        }
    }

    /// <summary>Show/hide tabs and change their order. Changes apply and save immediately.</summary>
    private FrameworkElement CustomizeView()
    {
        var list = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, VerticalAlignment = VerticalAlignment.Top };
        void Fill()
        {
            list.Children.Clear();
            foreach (var id in _tabs.Order)
            {
                if (!_pages.TryGetValue(id, out var page)) continue;
                var row = new DockPanel { Margin = new Thickness(0, 0, 16, 2) };

                var down = Ui.IconButton("", (_, _) => { if (_tabs.Move(id, +1)) Changed(); }, 24, 20, ghost: true);
                var up = Ui.IconButton("", (_, _) => { if (_tabs.Move(id, -1)) Changed(); }, 24, 20, ghost: true);
                down.ToolTip = "Move right";
                up.ToolTip = "Move left";
                DockPanel.SetDock(down, Dock.Right);
                DockPanel.SetDock(up, Dock.Right);
                row.Children.Add(down);
                row.Children.Add(up);

                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(new TextBlock { Text = page.Glyph, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 12, Foreground = Ui.Subtle, Width = 20, VerticalAlignment = VerticalAlignment.Center });
                label.Children.Add(Ui.Text(page.Title, 12));
                var check = new CheckBox { IsChecked = !_tabs.Hidden.Contains(id), Content = label };
                check.Click += (_, _) =>
                {
                    if (!_tabs.SetHidden(id, check.IsChecked != true)) check.IsChecked = true; // can't hide the last one
                    Changed();
                };
                row.Children.Add(check);
                list.Children.Add(row);
            }
        }

        void Changed()
        {
            _save();
            RebuildNav();
            Fill();
        }

        Fill();

        var done = new Button { Content = "Done", Padding = new Thickness(16, 3, 16, 3), Background = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Right };
        done.Click += (_, _) => Show(_current?.Id ?? _tabs.Visible[0]);
        var title = Ui.Title("Tabs — tick to show, arrows to reorder");

        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        title.Margin = new Thickness(4, 0, 0, 6);
        root.Children.Add(title);
        DockPanel.SetDock(done, Dock.Bottom);
        root.Children.Add(done);
        // Two columns so ten tabs fit without scrolling.
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Margin = new Thickness(4, 0, 4, 4) };
        root.Children.Add(scroll);
        return root;
    }
}
