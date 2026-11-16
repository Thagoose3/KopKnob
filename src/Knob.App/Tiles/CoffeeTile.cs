using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Knob.App.Platform;
using Knob.App.Shell;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>Keep-awake toggle. Always starts off; turned off on quit.</summary>
public sealed class CoffeeTile(NotchWindow window) : TileBase
{
    /// <summary>Raised when keep-awake is switched on or off.</summary>
    public event Action? Changed;

    public bool IsOn { get; private set; }

    public override string Id => "coffee";
    public override string Title => "Coffee";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.S, TileSize.W];

    public void Set(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        KeepAwake.Set(on);
        window.SetCoffeeDot(on);
        Changed?.Invoke();
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        var icon = Ui.Text("☕", size == TileSize.S ? 20 : 18);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var label = Ui.Text("", 11);
        label.HorizontalAlignment = HorizontalAlignment.Center;

        var content = new StackPanel { Orientation = size == TileSize.S ? Orientation.Vertical : Orientation.Horizontal };
        content.Children.Add(icon);
        if (size == TileSize.W) label.Margin = new Thickness(8, 0, 0, 0);
        content.Children.Add(label);

        var toggle = new ToggleButton { Content = content, ToolTip = "Keep the PC awake" };
        toggle.Click += (_, _) => Set(toggle.IsChecked == true);

        void Refresh()
        {
            toggle.IsChecked = IsOn;
            label.Text = IsOn ? "Awake" : "Coffee";
        }

        Ui.WhileLoaded(toggle, () => { Changed += Refresh; Refresh(); }, () => Changed -= Refresh);
        return toggle;
    }
}
