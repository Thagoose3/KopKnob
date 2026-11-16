using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Knob.Core;
using Knob.Core.Calendar;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>
/// Month view. Week start follows the current culture; text is English with the Gregorian calendar
/// (so a th-TH system still shows 2026, not 2569). Jumps back to the current month each time the panel opens.
/// </summary>
public sealed class CalendarTile(IClock clock) : TileBase
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private event Action? Changed;
    private int _year, _month;

    public override string Id => "calendar";
    public override string Title => "Calendar";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L];

    public override void OnPanelOpened()
    {
        var today = clock.Now;
        _year = today.Year;
        _month = today.Month;
        Changed?.Invoke();
    }

    private void Shift(int months)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(months);
        _year = d.Year;
        _month = d.Month;
        Changed?.Invoke();
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        if (_year == 0) { _year = clock.Now.Year; _month = clock.Now.Month; }
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

        var root = new DockPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var next = Ui.IconButton(Ui.IconRight, (_, _) => Shift(1), 20, 18, ghost: true);
        var prev = Ui.IconButton(Ui.IconLeft, (_, _) => Shift(-1), 20, 18, ghost: true);
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(prev, Dock.Right);
        header.Children.Add(next);
        header.Children.Add(prev);
        var title = Ui.Text("", 11, weight: FontWeights.SemiBold);
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var weekdays = new UniformGrid { Columns = 7 };
        for (var i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)firstDay + i) % 7);
            var t = Ui.Text(English.DateTimeFormat.GetShortestDayName(dow), 9, Ui.Subtle);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            weekdays.Children.Add(t);
        }
        DockPanel.SetDock(weekdays, Dock.Top);
        root.Children.Add(weekdays);

        var days = new UniformGrid { Columns = 7, Rows = 6 };
        root.Children.Add(days);

        void Refresh()
        {
            title.Text = new DateTime(_year, _month, 1).ToString("MMMM yyyy", English);
            var today = DateOnly.FromDateTime(clock.Now.DateTime);
            days.Children.Clear();
            foreach (var d in MonthGrid.Build(_year, _month, firstDay))
            {
                var isToday = d == today;
                var text = Ui.Text(d.Day.ToString(CultureInfo.InvariantCulture), 10,
                    isToday ? Ui.AccentFg : d.Month == _month ? Ui.Fg : Ui.Dim,
                    isToday ? FontWeights.Bold : null);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                days.Children.Add(new Border
                {
                    Background = isToday ? Ui.Accent : null,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(1, 0, 1, 0),
                    Child = text,
                });
            }
        }

        Ui.WhileLoaded(root, () => { Changed += Refresh; Refresh(); }, () => Changed -= Refresh);
        return root;
    }
}
