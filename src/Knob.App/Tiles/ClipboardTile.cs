using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.Core;
using Knob.Core.Clipboard;
using Knob.Core.Layout;
using Knob.Core.Storage;
using WpfClipboard = System.Windows.Clipboard;

namespace Knob.App.Tiles;

/// <summary>
/// Clipboard history (text only). Skips anything a password manager marks private.
/// Saves clipboard.json 1 s after the last change.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    // Formats apps use to say "don't record this" (password managers set these).
    private const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    private const string ViewerIgnoreFormat = "Clipboard Viewer Ignore";
    private const string CanIncludeFormat = "CanIncludeInClipboardHistory";

    private readonly JsonStore<ClipboardHistory> _store;
    private readonly IClock _clock;
    private readonly ClipboardListener _listener;
    private readonly DispatcherTimer _saveDebounce;
    private bool _paused;

    public ClipboardService(string path, int maxItems, IClock clock, Dispatcher dispatcher)
    {
        _store = new JsonStore<ClipboardHistory>(path);
        History = _store.Load();
        History.MaxItems = maxItems;
        _clock = clock;
        _saveDebounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _saveDebounce.Tick += (_, _) => Save();
        _listener = new ClipboardListener();
        _listener.Updated += OnClipboardUpdated;
    }

    public ClipboardHistory History { get; }

    public event Action? Changed;

    public bool Paused
    {
        get => _paused;
        set { _paused = value; Changed?.Invoke(); }
    }

    public void Dispose()
    {
        _listener.Dispose();
        Save();
    }

    public void Save()
    {
        _saveDebounce.Stop();
        try { _store.Save(History); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Put an item back on the clipboard. The resulting update just moves it to the top (no duplicate).</summary>
    public async void CopyBack(ClipboardItem item) => await ClipboardUtil.TrySetTextAsync(item.Text);

    public void TogglePin(long id) { if (History.Pin(id)) Commit(); }
    public void Remove(long id) { if (History.Remove(id)) Commit(); }
    public void Clear() { History.Clear(); Commit(); }

    private void Commit()
    {
        Changed?.Invoke();
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private async void OnClipboardUpdated()
    {
        if (_paused) return;
        var text = await ReadTextAsync();
        if (text is not null && History.Add(text, _clock.Now)) Commit();
    }

    /// <summary>Text on the clipboard, or null if none / private / still locked after 5 tries.</summary>
    private static async Task<string?> ReadTextAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var data = WpfClipboard.GetDataObject();
                if (data is null) return null;
                if (data.GetDataPresent(ExcludeFormat, false) || data.GetDataPresent(ViewerIgnoreFormat, false)) return null;
                if (data.GetDataPresent(CanIncludeFormat, false) && IsZeroDword(data.GetData(CanIncludeFormat, false))) return null;
                if (!data.GetDataPresent(DataFormats.UnicodeText, true)) return null;
                return data.GetData(DataFormats.UnicodeText, true) as string;
            }
            catch (COMException)
            {
                await Task.Delay(50); // another app has the clipboard open
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }

    private static bool IsZeroDword(object? value)
    {
        if (value is not MemoryStream ms || ms.Length < 4) return false;
        var bytes = new byte[4];
        ms.Position = 0;
        ms.ReadExactly(bytes);
        return BitConverter.ToInt32(bytes) == 0;
    }
}

public sealed class ClipboardTile(ClipboardService service) : TileBase
{
    public override string Id => "clipboard";
    public override string Title => "Clipboard";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L, TileSize.W];

    public override FrameworkElement CreateView(TileSize size)
    {
        var root = new DockPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var clear = new Button { Content = "Clear", Style = Ui.Res<Style>("GhostButton"), FontSize = 10 };
        clear.Click += (_, _) => service.Clear();
        var pause = new ToggleButton { Content = Ui.Text("Pause", 10), Padding = new Thickness(6, 1, 6, 1) };
        pause.Click += (_, _) => service.Paused = pause.IsChecked == true;
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(pause, Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(pause);
        header.Children.Add(Ui.Title("Clipboard"));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var list = new StackPanel();
        var empty = Ui.Text("Copied text shows up here", 11, Ui.Dim);
        empty.TextWrapping = TextWrapping.Wrap;
        var body = new Grid();
        body.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });
        body.Children.Add(empty);
        root.Children.Add(body);

        void Refresh()
        {
            pause.IsChecked = service.Paused;
            ((TextBlock)pause.Content).Text = service.Paused ? "Paused" : "Pause";
            list.Children.Clear();
            foreach (var item in service.History.Items) list.Children.Add(Row(item));
            empty.Visibility = list.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        Ui.WhileLoaded(root, () => { service.Changed += Refresh; Refresh(); }, () => service.Changed -= Refresh);
        return root;
    }

    private FrameworkElement Row(ClipboardItem item)
    {
        var id = item.Id;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };

        var del = Ui.IconButton(Ui.IconClose, (_, _) => service.Remove(id), 18, 18, ghost: true);
        ((TextBlock)del.Content).FontSize = 8;
        var pin = Ui.IconButton(item.Pinned ? "" : "", (_, _) => service.TogglePin(id), 18, 18, ghost: true);
        ((TextBlock)pin.Content).FontSize = 10;
        if (item.Pinned) pin.Foreground = Ui.Accent;
        DockPanel.SetDock(del, Dock.Right);
        DockPanel.SetDock(pin, Dock.Right);
        row.Children.Add(del);
        row.Children.Add(pin);

        var preview = item.Text.Length > 300 ? item.Text[..300] : item.Text;
        preview = preview.Replace("\r\n", " ⏎ ").Replace('\n', ' ').Replace('\t', ' ');
        var text = new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 2, 4, 2),
            Cursor = Cursors.Hand,
            ToolTip = "Click to copy",
            Child = new TextBlock { Text = preview, FontSize = 11, Foreground = Ui.Fg, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        text.MouseEnter += (_, _) => text.Background = Ui.Control;
        text.MouseLeave += (_, _) => text.Background = System.Windows.Media.Brushes.Transparent;
        text.MouseLeftButtonUp += (_, e) => { service.CopyBack(item); e.Handled = true; };
        row.Children.Add(text);
        return row;
    }
}
