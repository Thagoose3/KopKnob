using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Knob.App.Platform;
using Knob.Core.Layout;
using Knob.Core.Shelf;
using Knob.Core.Storage;

namespace Knob.App.Tiles;

/// <summary>Shared pieces of the Shelf and Launch pages: a saved path list and the icon+name item visual.</summary>
public abstract class PathListTile : TileBase
{
    private readonly JsonStore<PathList> _store;
    protected readonly PathList Items;

    protected PathListTile(string file, int max)
    {
        _store = new JsonStore<PathList>(file);
        var loaded = _store.Load();
        Items = new PathList { Max = max };
        Items.Add(loaded.Paths);
    }

    protected event Action? Changed;

    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L];

    public int Add(IEnumerable<string> paths)
    {
        var n = Items.Add(paths);
        if (n > 0) SaveAndNotify();
        return n;
    }

    protected void Remove(string path)
    {
        if (Items.Remove(path)) SaveAndNotify();
    }

    protected void SaveAndNotify()
    {
        try { _store.Save(Items); }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    /// <summary>Drop entries whose file/folder no longer exists.</summary>
    public override void OnPanelOpened()
    {
        if (Items.Prune(ShellIcons.Exists) > 0) SaveAndNotify();
    }

    /// <summary>Icon over a two-line name, highlighted on hover.</summary>
    protected static Border ItemVisual(string path, double width)
    {
        var icon = new Image { Source = ShellIcons.Get(path), Width = 32, Height = 32, Margin = new Thickness(0, 2, 0, 6) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var name = new TextBlock
        {
            Text = ShellIcons.DisplayName(path),
            FontSize = 11,
            Foreground = Ui.Fg,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 30,
        };
        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(name);
        var card = new Border
        {
            Width = width,
            Padding = new Thickness(6, 8, 6, 6),
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            Child = stack,
            ToolTip = path,
            Cursor = Cursors.Hand,
        };
        card.MouseEnter += (_, _) => card.Background = Ui.Res<Brush>("TileBrush");
        card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        return card;
    }

    protected static FrameworkElement EmptyHint(string title, string text)
    {
        var t = Ui.Text(title, 14, weight: FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var s = Ui.Text(text, 11.5, Ui.Subtle);
        s.HorizontalAlignment = HorizontalAlignment.Center;
        s.Margin = new Thickness(0, 4, 0, 0);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = Ui.IconShelf, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 26, Foreground = Ui.Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(t);
        stack.Children.Add(s);
        return new Border
        {
            BorderBrush = Ui.Dim,
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(6, 2, 6, 4),
            Child = stack,
        };
    }
}

/// <summary>
/// File shelf: drop files on the knob to park them here (references only, nothing is copied), then drag them out
/// to wherever they should go. Items that were moved away disappear from the shelf.
/// </summary>
public sealed class ShelfTile : PathListTile
{
    public ShelfTile(string? file = null) : base(file ?? KnobPaths.Shelf, max: 60) { }

    public override string Id => "shelf";
    public override string Title => "Shelf";

    public override FrameworkElement CreateView(TileSize size)
    {
        var count = Ui.Text("", 12, Ui.Subtle, FontWeights.SemiBold);
        var clear = new Button { Content = "Clear", Padding = new Thickness(12, 2, 12, 2), ToolTip = "Empty the shelf (the files stay where they are)" };
        clear.Click += (_, _) => { Items.Paths.Clear(); SaveAndNotify(); };
        var top = new DockPanel { Margin = new Thickness(4, 0, 0, 4) };
        DockPanel.SetDock(clear, Dock.Right);
        top.Children.Add(clear);
        top.Children.Add(count);

        var list = new WrapPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        var body = new Border();

        var root = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(body);
        Pages.Smooth(root);

        void Refresh()
        {
            count.Text = Items.Paths.Count == 0 ? "Shelf" : $"Shelf · {Items.Paths.Count} item{(Items.Paths.Count == 1 ? "" : "s")}";
            clear.Visibility = Items.Paths.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (Items.Paths.Count == 0)
            {
                body.Child = EmptyHint("Drop files here", "Park files on the knob, then drag them out to another folder or app");
                return;
            }
            list.Children.Clear();
            foreach (var p in Items.Paths) list.Children.Add(ShelfItem(p));
            body.Child = scroll;
        }

        Ui.WhileLoaded(root, () => { Changed += Refresh; Refresh(); }, () => Changed -= Refresh);
        Refresh();
        return root;
    }

    private FrameworkElement ShelfItem(string path)
    {
        var card = ItemVisual(path, 86);
        var remove = Ui.IconButton(Ui.IconClose, (_, _) => Remove(path), 20, 20, ghost: true);
        ((TextBlock)remove.Content).FontSize = 8;
        remove.ToolTip = "Remove from shelf";
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.Margin = new Thickness(0, 2, 2, 0);
        remove.Visibility = Visibility.Hidden;

        var host = new Grid();
        host.Children.Add(card);
        host.Children.Add(remove);
        host.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        host.MouseLeave += (_, _) => remove.Visibility = Visibility.Hidden;

        Point? down = null;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ShellIcons.Open(path); e.Handled = true; return; }
            down = e.GetPosition(card);
            e.Handled = true;
        };
        card.MouseLeftButtonUp += (_, _) => down = null;
        card.MouseMove += (_, e) =>
        {
            if (down is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(card) - start;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            var result = DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            // Moved somewhere else (or gone): it has left the shelf.
            if (result.HasFlag(DragDropEffects.Move) || !ShellIcons.Exists(path)) Remove(path);
        };
        card.ToolTip = $"{path}\nDrag out to move/copy · double-click to open";
        return host;
    }
}

/// <summary>Quick launch: pinned apps, files and folders. Click opens; right-click for more.</summary>
public sealed class LaunchTile : PathListTile
{
    private readonly Action _closePanel;
    private readonly Action<bool> _keepOpen;

    public LaunchTile(Action closePanel, Action<bool> keepOpen, string? file = null) : base(file ?? KnobPaths.Launch, max: 40)
    {
        _closePanel = closePanel;
        _keepOpen = keepOpen;
    }

    public override string Id => "launch";
    public override string Title => "Launch";

    /// <summary>The pinned paths, in order (for the quick search palette).</summary>
    public IReadOnlyList<string> Pinned => Items.Paths;

    public override FrameworkElement CreateView(TileSize size)
    {
        var list = new WrapPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        var hint = Ui.Text("Drop apps, files or folders here to pin them · right-click a pin to remove it", 10.5, Ui.Dim);
        hint.Margin = new Thickness(6, 2, 0, 0);
        DockPanel.SetDock(hint, Dock.Bottom);

        var root = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
        root.Children.Add(hint);
        root.Children.Add(scroll);
        Pages.Smooth(root);

        void Refresh()
        {
            list.Children.Clear();
            foreach (var p in Items.Paths) list.Children.Add(Pin(p));
            list.Children.Add(AddButton());
        }

        Ui.WhileLoaded(root, () => { Changed += Refresh; Refresh(); }, () => Changed -= Refresh);
        Refresh();
        return root;
    }

    private FrameworkElement Pin(string path)
    {
        var card = ItemVisual(path, 82);
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (ShellIcons.Open(path)) _closePanel();
        };

        var menu = new ContextMenu();
        MenuItem Item(string header, Action a)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => a();
            return mi;
        }
        menu.Items.Add(Item("Open", () => { if (ShellIcons.Open(path)) _closePanel(); }));
        menu.Items.Add(Item("Show in folder", () => { ShellIcons.ShowInFolder(path); _closePanel(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Move left", () => { if (Items.Move(path, -1)) SaveAndNotify(); }));
        menu.Items.Add(Item("Move right", () => { if (Items.Move(path, +1)) SaveAndNotify(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Unpin", () => Remove(path)));
        // The menu is its own popup window: keep the panel open while it shows.
        menu.Opened += (_, _) => _keepOpen(true);
        menu.Closed += (_, _) => _keepOpen(false);
        card.ContextMenu = menu;
        return card;
    }

    private FrameworkElement AddButton()
    {
        var plus = new TextBlock { Text = Ui.IconAdd, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 18, Foreground = Ui.Subtle, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 9, 0, 13) };
        var label = Ui.Text("Add", 11, Ui.Subtle);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel();
        stack.Children.Add(plus);
        stack.Children.Add(label);
        var card = new Border
        {
            Width = 82,
            Padding = new Thickness(6, 8, 6, 6),
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(12),
            BorderBrush = Ui.Dim,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = "Pin an app or file",
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pin to Knob",
                Multiselect = true,
                DereferenceLinks = false, // pin the shortcut itself, keeping its name and icon
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            };
            _keepOpen(true);
            try
            {
                if (dlg.ShowDialog() == true) Add(dlg.FileNames);
            }
            finally
            {
                _keepOpen(false);
            }
        };
        return card;
    }
}
