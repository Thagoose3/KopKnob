using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Knob.App.Tiles;

/// <summary>Swaps the panel between the tile grid and a full-panel tool page (Monitor now; Calc/QR in M3).</summary>
public interface IToolHost
{
    void ShowTool(FrameworkElement view, string title);
    void Back();
}

public sealed class PanelHost : Grid, IToolHost
{
    private readonly TileHost _tiles;
    private DockPanel? _tool;

    public PanelHost(TileHost tiles)
    {
        _tiles = tiles;
        Children.Add(tiles);
    }

    /// <summary>Raised when a tool page is closed (Back, or the panel collapsing).</summary>
    public event Action? ToolClosed;

    public bool IsToolOpen => _tool is not null;

    public void ShowTool(FrameworkElement view, string title)
    {
        Back();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var back = Ui.IconButton("", (_, _) => Back(), 28, 22);
        back.ToolTip = "Back";
        DockPanel.SetDock(back, Dock.Left);
        header.Children.Add(back);
        var t = Ui.Text(title, 12, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(6, 0, 0, 0);
        header.Children.Add(t);

        _tool = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(header, Dock.Top);
        _tool.Children.Add(header);
        _tool.Children.Add(view);

        _tiles.Visibility = Visibility.Collapsed;
        Children.Add(_tool);
    }

    public void Back()
    {
        if (_tool is null) return;
        Children.Remove(_tool);
        _tool = null;
        _tiles.Visibility = Visibility.Visible;
        ToolClosed?.Invoke();
    }
}
