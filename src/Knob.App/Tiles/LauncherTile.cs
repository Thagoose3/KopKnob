using System.Windows;
using System.Windows.Controls.Primitives;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>Buttons that open full-panel tools. M2: System monitor. M3 adds Calculator, QR and Text capture.</summary>
public sealed class LauncherTile(IReadOnlyList<LauncherTile.Entry> entries) : TileBase
{
    public sealed record Entry(string Glyph, string Tooltip, Action Open);

    public override string Id => "launcher";
    public override string Title => "Tools";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.S, TileSize.W];

    public override FrameworkElement CreateView(TileSize size)
    {
        var grid = new UniformGrid { Columns = size == TileSize.S ? 2 : 4, Rows = 2 };
        foreach (var e in entries)
        {
            var b = Ui.IconButton(e.Glyph, (_, _) => e.Open(), double.NaN, double.NaN);
            ((System.Windows.Controls.TextBlock)b.Content).FontSize = 13;
            b.Margin = new Thickness(2);
            b.ToolTip = e.Tooltip;
            grid.Children.Add(b);
        }
        return grid;
    }
}
