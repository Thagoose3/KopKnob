using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Knob.App.Platform;
using Knob.Core.Layout;
using Knob.Core.Weather;

namespace Knob.App.Tiles;

/// <summary>
/// Weather page: current conditions, today's high/low and the next six hours. The place is picked by searching a
/// city name (stored in settings). Refreshes on open when the data is older than 30 minutes.
/// </summary>
public sealed class WeatherTile : TileBase
{
    private static readonly FontFamily EmojiFont = new("Segoe UI Emoji, Segoe UI Symbol");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly WeatherService _service;
    private readonly Action _saveSettings;

    public WeatherTile(WeatherService service, Action saveSettings)
    {
        _service = service;
        _saveSettings = saveSettings;
    }

    public override string Id => "weather";
    public override string Title => "Weather";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L];

    public override void OnPanelOpened() => _ = _service.RefreshAsync();

    public static string Emoji(WeatherKind kind, bool isDay) => kind switch
    {
        WeatherKind.Clear => isDay ? "☀" : "\U0001F319",
        WeatherKind.PartlyCloudy => isDay ? "⛅" : "☁",
        WeatherKind.Overcast => "☁",
        WeatherKind.Fog => "\U0001F32B",
        WeatherKind.Drizzle => "\U0001F326",
        WeatherKind.Rain => "\U0001F327",
        WeatherKind.Snow => "❄",
        WeatherKind.Thunder => "⛈",
        _ => "·",
    };

    /// <summary>"☀ 31°" for the pill, or null when there is nothing to show.</summary>
    public static string? PillText(WeatherReport? r) => r is null ? null : $"{Emoji(r.Kind, r.IsDay)} {Math.Round(r.Temperature):0}°";

    public override FrameworkElement CreateView(TileSize size)
    {
        var body = new Border();
        var searching = false;

        var root = new Grid { Margin = new Thickness(6, 0, 6, 0) };
        root.Children.Add(body);
        Pages.Smooth(root);

        void Render()
        {
            if (searching || !_service.Settings.HasPlace)
            {
                if (body.Child is not FrameworkElement { Tag: "search" }) body.Child = SearchView();
                return;
            }
            body.Child = ReportView();
        }

        FrameworkElement ReportView()
        {
            var place = Ui.Text(_service.Settings.City ?? "", 12, Ui.Subtle, FontWeights.SemiBold);
            place.TextTrimming = TextTrimming.CharacterEllipsis;
            var edit = Ui.IconButton(Ui.IconEdit, (_, _) => { searching = true; Render(); }, 26, 22, ghost: true);
            edit.ToolTip = "Change place";
            var refresh = Ui.IconButton(Ui.IconRefresh, (_, _) => _ = _service.RefreshAsync(force: true), 26, 22, ghost: true);
            refresh.ToolTip = "Refresh";
            var top = new DockPanel { Margin = new Thickness(4, 0, 0, 4) };
            DockPanel.SetDock(refresh, Dock.Right);
            DockPanel.SetDock(edit, Dock.Right);
            top.Children.Add(refresh);
            top.Children.Add(edit);
            top.Children.Add(place);

            var r = _service.Report;
            FrameworkElement main;
            if (r is null)
            {
                main = Centered(Ui.Text(_service.Busy ? "Loading weather…" : _service.Error ?? "No data yet", 13, Ui.Subtle));
            }
            else
            {
                var icon = new TextBlock { Text = Emoji(r.Kind, r.IsDay), FontFamily = EmojiFont, FontSize = 46, Foreground = Ui.Fg, VerticalAlignment = VerticalAlignment.Center };
                var temp = new TextBlock
                {
                    Text = $"{Math.Round(r.Temperature):0}°",
                    FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                    FontSize = 58,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Ui.Fg,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var big = new StackPanel { Orientation = Orientation.Horizontal };
                big.Children.Add(icon);
                big.Children.Add(temp);

                var line1 = Ui.Text($"{r.Text}  ·  H {Math.Round(r.TodayMax):0}°  L {Math.Round(r.TodayMin):0}°", 13.5, weight: FontWeights.SemiBold);
                var line2 = Ui.Text($"Feels {Math.Round(r.FeelsLike):0}°  ·  Humidity {r.Humidity}%  ·  Wind {Math.Round(r.WindKmh):0} km/h", 11.5, Ui.Subtle);
                line2.Margin = new Thickness(0, 3, 0, 0);
                var now = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                now.Children.Add(big);
                now.Children.Add(line1);
                now.Children.Add(line2);
                if (_service.Error is string err)
                {
                    var e = Ui.Text(err, 10.5, Ui.Dim);
                    e.Margin = new Thickness(0, 4, 0, 0);
                    now.Children.Add(e);
                }

                var hours = new UniformGrid1(r.Hours.Count);
                foreach (var h in r.Hours)
                {
                    var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    var t = Ui.Text(h.Time.ToString("HH:mm", English), 10.5, Ui.Subtle);
                    t.HorizontalAlignment = HorizontalAlignment.Center;
                    var ic = new TextBlock { Text = Emoji(h.Kind, h.IsDay), FontFamily = EmojiFont, FontSize = 20, Foreground = Ui.Fg, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
                    var tt = Ui.Text($"{Math.Round(h.Temperature):0}°", 13, weight: FontWeights.SemiBold);
                    tt.HorizontalAlignment = HorizontalAlignment.Center;
                    col.Children.Add(t);
                    col.Children.Add(ic);
                    col.Children.Add(tt);
                    hours.Children.Add(col);
                }
                var hoursCard = Pages.Card(hours);
                hoursCard.Width = 330;
                hoursCard.VerticalAlignment = VerticalAlignment.Center;
                hoursCard.Padding = new Thickness(8, 12, 8, 12);

                var grid = new DockPanel();
                DockPanel.SetDock(hoursCard, Dock.Right);
                grid.Children.Add(hoursCard);
                grid.Children.Add(now);
                main = grid;
            }

            var page = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            page.Children.Add(top);
            page.Children.Add(main);
            return page;
        }

        FrameworkElement SearchView()
        {
            var box = new TextBox { FontSize = 13, Padding = new Thickness(4, 3, 4, 3), VerticalContentAlignment = VerticalAlignment.Center };
            var go = new Button { Content = "Search", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0), Background = Ui.Accent, Foreground = Ui.AccentFg };
            var results = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var status = Ui.Text("Type a city and press Enter. Only the city name (to find it) and its rounded location are sent to open-meteo.com.", 10.5, Ui.Dim);
            status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 8, 0, 0);

            async void Search()
            {
                var q = box.Text.Trim();
                if (q.Length == 0) return;
                results.Children.Clear();
                results.Children.Add(Ui.Text("Searching…", 12, Ui.Subtle));
                var places = await WeatherService.SearchAsync(q);
                results.Children.Clear();
                if (places.Count == 0) results.Children.Add(Ui.Text("No places found (or no internet).", 12, Ui.Subtle));
                foreach (var p in places)
                {
                    var b = new Button
                    {
                        Content = Ui.Text(p.Label, 12.5),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 4, 10, 4),
                        Margin = new Thickness(0, 0, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MinWidth = 260,
                    };
                    b.Click += (_, _) =>
                    {
                        _service.SetPlace(p);
                        _saveSettings();
                        searching = false;
                        Render();
                        _ = _service.RefreshAsync(force: true);
                    };
                    results.Children.Add(b);
                }
            }

            go.Click += (_, _) => Search();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Search(); e.Handled = true; } };

            var row = new DockPanel();
            DockPanel.SetDock(go, Dock.Right);
            row.Children.Add(go);
            if (_service.Settings.HasPlace)
            {
                var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
                cancel.Click += (_, _) => { searching = false; Render(); };
                DockPanel.SetDock(cancel, Dock.Right);
                row.Children.Add(cancel);
            }
            row.Children.Add(Ui.WithHint(box, "City, e.g. Bangkok"));

            var stack = new StackPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "search" };
            stack.Children.Add(Ui.Text("Where should the weather be for?", 15, weight: FontWeights.SemiBold));
            var spacer = new Border { Height = 10 };
            stack.Children.Add(spacer);
            stack.Children.Add(row);
            stack.Children.Add(status);
            stack.Children.Add(new ScrollViewer { Content = results, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });
            return stack;
        }

        void OnChanged()
        {
            if (!searching) Render();
        }

        Ui.WhileLoaded(root, () => { _service.Changed += OnChanged; Render(); }, () => _service.Changed -= OnChanged);
        Render();
        return root;
    }

    private static FrameworkElement Centered(UIElement child) =>
        new Grid { Children = { child }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>One row, equal columns.</summary>
    private sealed class UniformGrid1 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid1(int columns)
        {
            Rows = 1;
            Columns = Math.Max(1, columns);
        }
    }
}
