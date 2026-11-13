using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>
/// Edit-mode page. Works on a copy of the layout: tiles are shown as cards that can be dragged to another cell
/// (green outline = fits, red = blocked → snaps back), resized with S/W/L (only sizes the tile supports) and hidden
/// with ✕. Hidden tiles sit in the bottom strip and can be dragged back onto the grid.
/// Done hands the copy back; Reset restores the default layout (still needs Done).
/// </summary>
public sealed class LayoutEditor : DockPanel
{
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x7C, 0x5C, 0xFF));
    private static readonly Brush BadBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xEF, 0x44, 0x44));

    private readonly TileRegistry _registry;
    private readonly Action<GridLayout> _done;
    private readonly Grid _grid = new() { Background = Brushes.Transparent };
    private readonly WrapPanel _hidden = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _ghost = new()
    {
        CornerRadius = new CornerRadius(12),
        Margin = new Thickness(3),
        BorderThickness = new Thickness(2),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };
    private GridLayout _layout;

    // drag state
    private string? _dragId;
    private TileSize _dragSize;
    private bool _dragFromHidden;
    private Point _grabOffset;       // pointer offset inside the dragged card (px)
    private FrameworkElement? _dragVisual;
    private (int Col, int Row)? _target;

    static LayoutEditor()
    {
        OkBrush.Freeze();
        BadBrush.Freeze();
    }

    public LayoutEditor(TileRegistry registry, GridLayout working, Action<GridLayout> done)
    {
        _registry = registry;
        _layout = working;
        _done = done;

        for (var c = 0; c < GridLayout.Columns; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < GridLayout.Rows; r++) _grid.RowDefinitions.Add(new RowDefinition());

        // Bottom strip: hidden tiles + Reset / Done
        var strip = new DockPanel { Margin = new Thickness(3, 6, 3, 0), Height = 34 };
        var doneButton = new Button { Content = "Done", Padding = new Thickness(16, 4, 16, 4), Background = Ui.Accent };
        doneButton.Click += (_, _) => _done(_layout);
        var reset = new Button { Content = "Reset", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        reset.Click += (_, _) => { _layout = GridLayout.Default(); Rebuild(); };
        DockPanel.SetDock(doneButton, Dock.Right);
        DockPanel.SetDock(reset, Dock.Right);
        strip.Children.Add(doneButton);
        strip.Children.Add(reset);
        var hiddenLabel = Ui.Text("Hidden:", 10, Ui.Subtle);
        hiddenLabel.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(hiddenLabel, Dock.Left);
        strip.Children.Add(hiddenLabel);
        strip.Children.Add(_hidden);
        DockPanel.SetDock(strip, Dock.Bottom);
        Children.Add(strip);
        Children.Add(_grid);

        MouseMove += OnDragMove;
        MouseLeftButtonUp += OnDragEnd;
        LostMouseCapture += (_, _) => CancelDrag();

        Rebuild();
    }

    /// <summary>Layout being edited (for tests / inspection).</summary>
    public GridLayout Working => _layout;

    private void Rebuild()
    {
        _grid.Children.Clear();
        foreach (var p in _layout.Placements)
        {
            if (!_registry.TryGet(p.TileId, out var tile)) continue;
            var card = Card(tile, p);
            var (w, h) = TileSizes.Span(p.Size);
            Grid.SetColumn(card, p.Col);
            Grid.SetRow(card, p.Row);
            Grid.SetColumnSpan(card, w);
            Grid.SetRowSpan(card, h);
            _grid.Children.Add(card);
        }
        _grid.Children.Add(_ghost);

        _hidden.Children.Clear();
        var shown = _layout.Placements.Select(p => p.TileId).ToHashSet();
        foreach (var tile in _registry.All.Where(t => !shown.Contains(t.Id)))
            _hidden.Children.Add(Chip(tile));
        if (_hidden.Children.Count == 0) _hidden.Children.Add(Ui.Text("none", 10, Ui.Dim));
    }

    private FrameworkElement Card(ITile tile, TilePlacement p)
    {
        var title = Ui.Text(tile.Title, 11, weight: FontWeights.SemiBold);
        var hide = Ui.IconButton(Ui.IconClose, (_, _) => { _layout.Hide(tile.Id); Rebuild(); }, 18, 16, ghost: true);
        ((TextBlock)hide.Content).FontSize = 8;
        hide.ToolTip = "Hide";
        var top = new DockPanel();
        DockPanel.SetDock(hide, Dock.Right);
        top.Children.Add(hide);
        top.Children.Add(title);

        var sizes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var s in tile.SupportedSizes.OrderBy(s => s))
        {
            var b = new Button
            {
                Content = Ui.Text(s.ToString(), 10),
                Width = 22,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 3, 0),
                Background = s == p.Size ? Ui.Accent : Ui.Control,
                ToolTip = s switch { TileSize.S => "Small 1×1", TileSize.W => "Wide 2×1", _ => "Large 2×2" },
            };
            var size = s;
            b.Click += (_, _) =>
            {
                if (_layout.TryResize(tile.Id, size)) Rebuild();
                else Flash(tile.Id, p, size);
            };
            sizes.Children.Add(b);
        }

        var body = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);
        body.Children.Add(sizes);

        var card = new Border
        {
            Background = Ui.Res<Brush>("TileBrush"),
            BorderBrush = Ui.Control,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(3),
            Padding = new Thickness(8, 6, 6, 6),
            Child = body,
            Cursor = Cursors.SizeAll,
            Tag = tile.Id,
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (IsOnButton(e.OriginalSource as DependencyObject)) return;
            BeginDrag(tile.Id, p.Size, fromHidden: false, card, e);
        };
        return card;
    }

    private FrameworkElement Chip(ITile tile)
    {
        var size = tile.SupportedSizes.OrderBy(s => s).First();
        var chip = new Border
        {
            Background = Ui.Control,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 2, 6, 2),
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag onto the grid",
            Child = Ui.Text(tile.Title, 11),
        };
        chip.MouseLeftButtonDown += (_, e) => BeginDrag(tile.Id, size, fromHidden: true, chip, e);
        return chip;
    }

    // ───────────── dragging ─────────────

    private void BeginDrag(string id, TileSize size, bool fromHidden, FrameworkElement visual, MouseButtonEventArgs e)
    {
        _dragId = id;
        _dragSize = size;
        _dragFromHidden = fromHidden;
        _dragVisual = visual;
        _grabOffset = fromHidden ? new Point(CellWidth / 2, CellHeight / 2) : e.GetPosition(visual);
        visual.Opacity = 0.5;
        CaptureMouse();
        e.Handled = true;
    }

    private double CellWidth => _grid.ActualWidth / GridLayout.Columns;
    private double CellHeight => _grid.ActualHeight / GridLayout.Rows;

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragId is null) return;
        var p = e.GetPosition(_grid);
        var inside = p.X >= 0 && p.Y >= 0 && p.X <= _grid.ActualWidth && p.Y <= _grid.ActualHeight;
        if (!inside || CellWidth <= 0)
        {
            _target = null;
            _ghost.Visibility = Visibility.Collapsed;
            return;
        }

        // Top-left of the dragged card snapped to the nearest cell.
        var col = (int)Math.Round((p.X - _grabOffset.X) / CellWidth);
        var row = (int)Math.Round((p.Y - _grabOffset.Y) / CellHeight);
        var (w, h) = TileSizes.Span(_dragSize);
        col = Math.Clamp(col, 0, GridLayout.Columns - 1);
        row = Math.Clamp(row, 0, GridLayout.Rows - 1);
        _target = (col, row);

        var ok = _layout.CanPlace(_dragId, col, row, _dragSize);
        _ghost.Background = ok ? OkBrush : BadBrush;
        _ghost.BorderBrush = ok ? Ui.Accent : Brushes.IndianRed;
        Grid.SetColumn(_ghost, col);
        Grid.SetRow(_ghost, row);
        // Show the true footprint, even where it would stick out (clipped by the grid).
        Grid.SetColumnSpan(_ghost, Math.Min(w, GridLayout.Columns - col));
        Grid.SetRowSpan(_ghost, Math.Min(h, GridLayout.Rows - row));
        _ghost.Visibility = Visibility.Visible;
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_dragId is null) return;
        var id = _dragId;
        var target = _target;
        EndDrag();
        if (target is { } t)
        {
            var changed = _dragFromHidden
                ? _layout.TryShow(id, _dragSize, t.Col, t.Row)
                : _layout.TryMove(id, t.Col, t.Row);
            if (changed) Rebuild(); // otherwise it simply stays where it was ("snaps back")
        }
    }

    private void EndDrag()
    {
        if (_dragVisual is not null) _dragVisual.Opacity = 1;
        _dragId = null;
        _dragVisual = null;
        _target = null;
        _ghost.Visibility = Visibility.Collapsed;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void CancelDrag()
    {
        if (_dragId is not null) EndDrag();
    }

    /// <summary>Resize refused: flash the would-be footprint red for a moment.</summary>
    private async void Flash(string id, TilePlacement p, TileSize size)
    {
        var (w, h) = TileSizes.Span(size);
        _ghost.Background = BadBrush;
        _ghost.BorderBrush = Brushes.IndianRed;
        Grid.SetColumn(_ghost, p.Col);
        Grid.SetRow(_ghost, p.Row);
        Grid.SetColumnSpan(_ghost, Math.Min(w, GridLayout.Columns - p.Col));
        Grid.SetRowSpan(_ghost, Math.Min(h, GridLayout.Rows - p.Row));
        _ghost.Visibility = Visibility.Visible;
        await Task.Delay(450);
        if (_dragId is null) _ghost.Visibility = Visibility.Collapsed;
    }

    private static bool IsOnButton(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ButtonBase) return true;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}
