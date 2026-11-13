using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>Draws tiles on an 8×3 grid according to a <see cref="GridLayout"/>. Unknown tile ids are skipped.</summary>
public sealed class TileHost : Grid
{
    private readonly TileRegistry _registry;
    private readonly List<ITile> _placed = [];
    private GridLayout _layout;

    public TileHost(TileRegistry registry, GridLayout layout)
    {
        _registry = registry;
        _layout = layout;
        for (var c = 0; c < GridLayout.Columns; c++) ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < GridLayout.Rows; r++) RowDefinitions.Add(new RowDefinition());
        Build();
    }

    public GridLayout Layout
    {
        get => _layout;
        set { _layout = value; Build(); }
    }

    public void OnPanelOpened()
    {
        foreach (var t in _placed) Safe(t.OnPanelOpened);
    }

    public void OnPanelClosed()
    {
        foreach (var t in _placed) Safe(t.OnPanelClosed);
    }

    private void Build()
    {
        Children.Clear();
        _placed.Clear();

        foreach (var p in _layout.Placements)
        {
            if (!_registry.TryGet(p.TileId, out var tile)) continue;

            var size = tile.SupportedSizes.Contains(p.Size) ? p.Size : tile.SupportedSizes[0];
            FrameworkElement view;
            try
            {
                view = tile.CreateView(size);
            }
            catch (Exception)
            {
                view = new TextBlock
                {
                    Text = $"{tile.Title}: error",
                    Foreground = (Brush)FindResource("SubtleForegroundBrush"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                };
            }

            var chrome = new Border
            {
                Background = (Brush)FindResource("TileBrush"),
                CornerRadius = (CornerRadius)FindResource("TileCornerRadius"),
                Margin = new Thickness(3),
                Padding = new Thickness(8),
                Child = view,
                ClipToBounds = true,
            };
            var (w, h) = TileSizes.Span(p.Size);
            SetColumn(chrome, p.Col);
            SetRow(chrome, p.Row);
            SetColumnSpan(chrome, w);
            SetRowSpan(chrome, h);
            Children.Add(chrome);
            _placed.Add(tile);
        }
    }

    private static void Safe(Action a)
    {
        try { a(); } catch (Exception) { /* one tile must not break the panel */ }
    }
}
