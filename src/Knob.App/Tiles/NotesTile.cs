using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Knob.Core.Layout;
using Knob.Core.Storage;

namespace Knob.App.Tiles;

/// <summary>Free text saved to notes.txt: 500 ms after the last keystroke, on panel close, and on quit.</summary>
public sealed class NotesTile : TileBase
{
    private readonly string _path;
    private readonly DispatcherTimer _debounce;
    private string _text;
    private bool _dirty;

    public NotesTile(string path, Dispatcher dispatcher)
    {
        _path = path;
        _text = AtomicFile.ReadAllTextOrDefault(path);
        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) => Save();
    }

    public override string Id => "notes";
    public override string Title => "Notes";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.W, TileSize.L];

    public override void OnPanelClosed() => Save();

    public void Save()
    {
        _debounce.Stop();
        if (!_dirty) return;
        try
        {
            AtomicFile.WriteAllText(_path, _text);
            _dirty = false;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // keep _dirty; next save retries
        }
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        var box = new TextBox
        {
            Text = _text,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.TextChanged += (_, _) =>
        {
            if (box.Text == _text) return;
            _text = box.Text;
            _dirty = true;
            _debounce.Stop();
            _debounce.Start();
        };
        return Ui.WithHint(box, "Notes…");
    }
}
