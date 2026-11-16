using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Knob.App.Shell;
using Knob.Core;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>Digital clock + date. Refreshed on panel open and on the shared minute tick while open.</summary>
public sealed class ClockTile(IClock clock, PillStatus pill) : TileBase
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    /// <summary>Raised when the page opens and once a minute while it is on screen.</summary>
    public event Action? Refreshed;
    private bool _open;

    public IClock Clock => clock;

    public override string Id => "clock";
    public override string Title => "Clock";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.S, TileSize.W];

    public override void OnPanelOpened()
    {
        _open = true;
        pill.MinuteTick += OnMinute;
        Refreshed?.Invoke();
    }

    public override void OnPanelClosed()
    {
        _open = false;
        pill.MinuteTick -= OnMinute;
    }

    private void OnMinute()
    {
        if (_open) Refreshed?.Invoke();
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        var time = Ui.Text("", size == TileSize.S ? 22 : 26, weight: FontWeights.SemiBold);
        var date = Ui.Text("", 11, Ui.Subtle);
        time.HorizontalAlignment = date.HorizontalAlignment = HorizontalAlignment.Center;
        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(time);
        root.Children.Add(date);

        void Refresh()
        {
            var now = clock.Now;
            time.Text = now.ToString("HH:mm", English);
            date.Text = now.ToString(size == TileSize.S ? "ddd d MMM" : "dddd d MMMM", English);
        }

        Ui.WhileLoaded(root, () => { Refreshed += Refresh; Refresh(); }, () => Refreshed -= Refresh);
        return root;
    }
}
