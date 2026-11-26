using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.App.Tools;
using Knob.Core.Convert;
using Knob.Core.Layout;
using Knob.Core.Storage;

namespace Knob.App.Tiles;

/// <summary>
/// File converter page: drop one or more files (images, audio/video, JSON/CSV/TSV, Markdown), pick a target format
/// from context-aware chips, convert next to the originals without overwriting, and drag the results straight out
/// into any app.
/// </summary>
public sealed class ConvertTile : TileBase
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));

    private readonly FileConverterService _converter = new();
    private readonly Action<bool> _keepOpen;
    private readonly Action<string> _notify;
    private readonly Dispatcher _dispatcher;

    private readonly List<string> _inputs = [];
    private List<ConvertResultItem> _results = [];
    private ConvertFormat? _selectedTarget;
    private int _quality = 90;
    private bool _mergePdf = true;
    private bool _busy;
    private double _progress;
    private string? _statusHint;
    private CancellationTokenSource? _cts;
    private bool _draggingOut;

    public ConvertTile(Action<bool> keepOpen, Action<string> notify, Dispatcher dispatcher)
    {
        _keepOpen = keepOpen;
        _notify = notify;
        _dispatcher = dispatcher;
    }

    private event Action? Changed;

    public override string Id => "convert";
    public override string Title => "Convert";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L];

    public override void OnPanelOpened()
    {
        if (_busy) return;
        var before = _inputs.Count;
        _inputs.RemoveAll(p => !File.Exists(p));
        if (_inputs.Count != before)
        {
            SyncSelectedTarget();
            Changed?.Invoke();
        }
    }

    /// <summary>Accept files dropped onto Knob while the Convert page is active.</summary>
    public void ReceiveFiles(IEnumerable<string> paths)
    {
        if (_draggingOut || _busy) return;

        var incoming = paths
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (incoming.Count == 0) return;

        var supported = incoming.Where(ConvertPlan.IsSupported).ToList();
        var skipped = incoming.Count - supported.Count;

        if (supported.Count == 0)
        {
            _statusHint = "Unsupported format — try PNG/JPG/WEBP/ICO, MP4/MP3/WAV, JSON/CSV, or MD";
            Changed?.Invoke();
            return;
        }

        // Keep only incoming files compatible with the first supported file in this drop.
        var validBatch = new List<string> { supported[0] };
        for (var i = 1; i < supported.Count; i++)
        {
            var probe = new List<string>(validBatch) { supported[i] };
            if (ConvertPlan.AvailableTargets(probe).Count > 0) validBatch.Add(supported[i]);
            else skipped++;
        }

        // If a previous run finished, or the new drop is a different category from what's queued, start fresh.
        var canAppend = _results.Count == 0 && _inputs.Count > 0
                        && ConvertPlan.AvailableTargets([.. _inputs, .. validBatch]).Count > 0;
        if (!canAppend)
        {
            _inputs.Clear();
            _results = [];
        }

        foreach (var f in validBatch)
        {
            if (!_inputs.Contains(f, StringComparer.OrdinalIgnoreCase))
                _inputs.Add(f);
        }

        _statusHint = skipped > 0 ? $"Skipped {skipped} incompatible file{(skipped == 1 ? "" : "s")}" : null;
        SyncSelectedTarget();
        Changed?.Invoke();
    }

    private void SyncSelectedTarget()
    {
        var targets = ConvertPlan.AvailableTargets(_inputs);
        if (targets.Count == 0)
        {
            _selectedTarget = null;
            return;
        }
        if (_selectedTarget is null || !targets.Any(t => t.Id == _selectedTarget.Id))
            _selectedTarget = targets[0];
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        var host = new Border { Margin = new Thickness(6, 0, 6, 0) };
        Pages.Smooth(host);

        void Rebuild()
        {
            if (_results.Count > 0) host.Child = BuildResultsView();
            else if (_inputs.Count > 0) host.Child = BuildQueueView();
            else host.Child = BuildEmptyView();
        }

        Ui.WhileLoaded(host, () => { Changed += Rebuild; Rebuild(); }, () => Changed -= Rebuild);
        Rebuild();
        return host;
    }

    // ───────────────────────── Empty state ─────────────────────────

    private FrameworkElement BuildEmptyView()
    {
        var icon = new TextBlock
        {
            Text = Ui.IconConvert,
            FontFamily = Ui.Res<FontFamily>("IconFont"),
            FontSize = 26,
            Foreground = Ui.Subtle,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var title = Ui.Text("Drop files here to convert", 14, weight: FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;

        var sub = Ui.Text("or click anywhere here to choose files", 11.5, Ui.Subtle);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.Margin = new Thickness(0, 3, 0, 0);

        var formats = Ui.Text("PNG · JPG · WEBP · ICO · PDF   |   MP4 · MP3 · WAV · M4A   |   JSON ↔ CSV · MD → HTML", 10.5, Ui.Dim);
        formats.HorizontalAlignment = HorizontalAlignment.Center;
        formats.Margin = new Thickness(0, 10, 0, 0);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(title);
        stack.Children.Add(sub);
        stack.Children.Add(formats);

        if (!string.IsNullOrEmpty(_statusHint))
        {
            var warn = Ui.Text(_statusHint, 10.5, ErrorBrush);
            warn.HorizontalAlignment = HorizontalAlignment.Center;
            warn.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(warn);
        }

        var box = new Border
        {
            BorderBrush = Ui.Dim,
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent,
            Margin = new Thickness(2, 2, 2, 4),
            Cursor = Cursors.Hand,
            Child = stack,
        };
        box.MouseEnter += (_, _) => box.Background = Ui.Res<Brush>("TileBrush");
        box.MouseLeave += (_, _) => box.Background = Brushes.Transparent;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; BrowseFiles(); };
        return box;
    }

    // ───────────────────────── Queue / Convert state ─────────────────────────

    private FrameworkElement BuildQueueView()
    {
        var targets = ConvertPlan.AvailableTargets(_inputs);
        SyncSelectedTarget();

        // Top bar
        var summary = _busy
            ? $"Converting… {(int)Math.Round(_progress * 100)}%"
            : $"{_inputs.Count} file{(_inputs.Count == 1 ? "" : "s")} ready"
              + (string.IsNullOrEmpty(_statusHint) ? "" : $" · {_statusHint}");
        var statusText = Ui.Text(summary, 11.5, Ui.Subtle, FontWeights.SemiBold);

        var top = new DockPanel { Margin = new Thickness(4, 0, 2, 4) };
        if (_busy)
        {
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 2, 12, 2) };
            cancel.Click += (_, _) => _cts?.Cancel();
            DockPanel.SetDock(cancel, Dock.Right);
            top.Children.Add(cancel);
        }
        else
        {
            var clear = new Button { Content = "Clear", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0) };
            clear.Click += (_, _) => ClearAll();
            var add = new Button { Content = "+ Add", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Add more files" };
            add.Click += (_, _) => BrowseFiles();
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(add, Dock.Right);
            top.Children.Add(clear);
            top.Children.Add(add);
        }
        top.Children.Add(statusText);

        // Bottom bar: target chips + quality / merge controls + Convert button
        var bottomStack = new StackPanel();
        var controlsRow = new DockPanel();

        if (_selectedTarget is ConvertFormat chosen)
        {
            var runBtn = new Button
            {
                Content = $"Convert → {chosen.Label}",
                Padding = new Thickness(14, 3, 14, 3),
                Height = 26,
                Background = Ui.Accent,
                Foreground = Ui.AccentFg,
                FontWeight = FontWeights.SemiBold,
                IsEnabled = !_busy,
            };
            runBtn.Click += (_, _) => _ = RunConvertAsync();
            DockPanel.SetDock(runBtn, Dock.Right);
            controlsRow.Children.Add(runBtn);
        }

        var chipsRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        chipsRow.Children.Add( MakeLabel("To", rightMargin: 6));
        foreach (var fmt in targets)
        {
            var active = _selectedTarget?.Id == fmt.Id;
            var chip = new Button
            {
                Content = fmt.Label,
                Padding = new Thickness(9, 2, 9, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Height = 24,
                MinWidth = 42,
                Background = active ? Ui.Accent : Ui.Control,
                Foreground = active ? Ui.AccentFg : Ui.Fg,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                IsEnabled = !_busy,
            };
            var targetFmt = fmt;
            chip.Click += (_, _) =>
            {
                _selectedTarget = targetFmt;
                Changed?.Invoke();
            };
            chipsRow.Children.Add(chip);
        }

        if (_selectedTarget is { SupportsQuality: true })
        {
            chipsRow.Children.Add(MakeLabel("·  Quality", leftMargin: 6, rightMargin: 6));
            foreach (var (label, q, tip) in new[] { ("80%", 80, "Smaller file size"), ("90%", 90, "Balanced quality & size"), ("100%", 100, "Lossless (WEBP) / Max (JPG)") })
            {
                var sel = _quality == q;
                var qb = new Button
                {
                    Content = label,
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 3, 0),
                    Height = 22,
                    FontSize = 11,
                    Background = sel ? Ui.Accent : Ui.Control,
                    Foreground = sel ? Ui.AccentFg : Ui.Subtle,
                    ToolTip = tip,
                    IsEnabled = !_busy,
                };
                var pickedQ = q;
                qb.Click += (_, _) => { _quality = pickedQ; Changed?.Invoke(); };
                chipsRow.Children.Add(qb);
            }
        }
        else if (_selectedTarget is { CanMergeImages: true } && _inputs.Count > 1)
        {
            chipsRow.Children.Add(MakeLabel("·", leftMargin: 6, rightMargin: 6));
            var mergeCheck = new CheckBox
            {
                Content = Ui.Text("Merge into 1 PDF", 11.5, Ui.Fg),
                IsChecked = _mergePdf,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !_busy,
            };
            mergeCheck.Click += (_, _) => _mergePdf = mergeCheck.IsChecked == true;
            chipsRow.Children.Add(mergeCheck);
        }

        controlsRow.Children.Add(chipsRow);
        bottomStack.Children.Add(controlsRow);

        if (_busy)
        {
            var (bar, setBar) = Pages.Bar(Ui.Accent, 4);
            bar.Margin = new Thickness(0, 7, 0, 0);
            setBar(_progress);
            bottomStack.Children.Add(bar);
        }

        var bottomCard = Pages.Card(bottomStack, new Thickness(0, 4, 0, 2));
        bottomCard.Padding = new Thickness(10, 7, 10, 7);

        // Queued files list
        var list = new WrapPanel();
        for (var i = 0; i < _inputs.Count; i++)
            list.Children.Add(QueuedItemCard(_inputs[i], i));
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottomCard, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottomCard);
        root.Children.Add(scroll);
        return root;
    }

    private FrameworkElement QueuedItemCard(string path, int index)
    {
        var icon = new Image { Source = ShellIcons.Get(path), Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var name = Ui.Text(Path.GetFileName(path), 11.5, weight: FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var sizeStr = TryFileLength(path) is long len ? ByteFormatter.Format(len) : "";
        var meta = Ui.Text(sizeStr, 10, Ui.Dim);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(name);
        texts.Children.Add(meta);

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        if (!_busy)
        {
            var remove = Ui.IconButton(Ui.IconClose, (_, _) =>
            {
                _inputs.Remove(path);
                _statusHint = null;
                SyncSelectedTarget();
                Changed?.Invoke();
            }, 20, 20, ghost: true);
            ((TextBlock)remove.Content).FontSize = 8;
            remove.ToolTip = "Remove";
            remove.Visibility = Visibility.Hidden;
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);

            var card = new Border
            {
                Width = 216,
                Padding = new Thickness(8, 6, 6, 6),
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(10),
                Background = Ui.Res<Brush>("TileBrush"),
                Child = row,
                ToolTip = path,
            };
            card.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
            card.MouseLeave += (_, _) => remove.Visibility = Visibility.Hidden;

            if (_inputs.Count > 1)
            {
                var menu = new ContextMenu();
                if (index > 0)
                {
                    var left = new MenuItem { Header = "Move earlier" };
                    left.Click += (_, _) => { (_inputs[index - 1], _inputs[index]) = (_inputs[index], _inputs[index - 1]); Changed?.Invoke(); };
                    menu.Items.Add(left);
                }
                if (index < _inputs.Count - 1)
                {
                    var right = new MenuItem { Header = "Move later" };
                    right.Click += (_, _) => { (_inputs[index + 1], _inputs[index]) = (_inputs[index], _inputs[index + 1]); Changed?.Invoke(); };
                    menu.Items.Add(right);
                }
                var rm = new MenuItem { Header = "Remove" };
                rm.Click += (_, _) => { _inputs.Remove(path); SyncSelectedTarget(); Changed?.Invoke(); };
                menu.Items.Add(rm);
                menu.Opened += (_, _) => _keepOpen(true);
                menu.Closed += (_, _) => _keepOpen(false);
                card.ContextMenu = menu;
            }

            row.Children.Add(texts);
            return card;
        }

        row.Children.Add(texts);
        return new Border
        {
            Width = 216,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Res<Brush>("TileBrush"),
            Child = row,
            ToolTip = path,
        };
    }

    // ───────────────────────── Results / Drag-out state ─────────────────────────

    private FrameworkElement BuildResultsView()
    {
        var ok = _results.Where(r => r.Succeeded).ToList();
        var failed = _results.Count - ok.Count;
        var headline = failed == 0
            ? $"✓ Converted {ok.Count} file{(ok.Count == 1 ? "" : "s")} · drag out to any app, or double-click to open"
            : $"{ok.Count} converted, {failed} failed";
        var status = Ui.Text(headline, 11.5, failed > 0 && ok.Count == 0 ? ErrorBrush : Ui.Subtle, FontWeights.SemiBold);

        var top = new DockPanel { Margin = new Thickness(4, 0, 2, 4) };
        var clear = new Button { Content = "Clear", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0) };
        clear.Click += (_, _) => ClearAll();
        DockPanel.SetDock(clear, Dock.Right);
        top.Children.Add(clear);

        var back = new Button { Content = "← Again", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Convert the same source files to another format" };
        back.Click += (_, _) => { _results = []; Changed?.Invoke(); };
        DockPanel.SetDock(back, Dock.Right);
        top.Children.Add(back);

        if (ok.Count > 0)
        {
            var reveal = new Button { Content = "Show in folder", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Highlight in File Explorer" };
            reveal.Click += (_, _) => ShellIcons.ShowInFolder(ok[0].OutputPath!);
            DockPanel.SetDock(reveal, Dock.Right);
            top.Children.Add(reveal);
        }
        top.Children.Add(status);

        var list = new WrapPanel();
        foreach (var item in _results)
            list.Children.Add(ResultCard(item));

        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(scroll);
        return root;
    }

    private FrameworkElement ResultCard(ConvertResultItem item)
    {
        if (!item.Succeeded || item.OutputPath is not string outPath)
        {
            var errTitle = Ui.Text(Path.GetFileName(item.SourcePath), 11, weight: FontWeights.SemiBold);
            errTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            var errMsg = Ui.Text(item.Error ?? "Conversion failed", 10, ErrorBrush);
            errMsg.TextWrapping = TextWrapping.Wrap;
            errMsg.MaxHeight = 32;
            var errStack = new StackPanel();
            errStack.Children.Add(errTitle);
            errStack.Children.Add(errMsg);
            return new Border
            {
                Width = 170,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(12),
                Background = Ui.Res<Brush>("TileBrush"),
                BorderBrush = ErrorBrush,
                BorderThickness = new Thickness(1),
                Child = errStack,
                ToolTip = item.Error,
            };
        }

        var icon = new Image { Source = ShellIcons.Get(outPath), Width = 32, Height = 32, Margin = new Thickness(0, 2, 0, 5) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var name = new TextBlock
        {
            Text = Path.GetFileName(outPath),
            FontSize = 11,
            Foreground = Ui.Fg,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 30,
        };
        var size = Ui.Text(TryFileLength(outPath) is long len ? ByteFormatter.Format(len) : "", 10, Ui.Subtle);
        size.HorizontalAlignment = HorizontalAlignment.Center;
        size.Margin = new Thickness(0, 2, 0, 0);

        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(name);
        stack.Children.Add(size);

        var card = new Border
        {
            Width = 104,
            Padding = new Thickness(6, 8, 6, 6),
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(12),
            Background = Ui.Res<Brush>("TileBrush"),
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = $"{outPath}\nDrag out to another app · double-click to open · right-click for more",
        };
        card.MouseEnter += (_, _) => card.Background = Ui.Control;
        card.MouseLeave += (_, _) => card.Background = Ui.Res<Brush>("TileBrush");

        Point? down = null;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                ShellIcons.Open(outPath);
                e.Handled = true;
                return;
            }
            down = e.GetPosition(card);
            e.Handled = true;
        };
        card.MouseLeftButtonUp += (_, _) => down = null;
        card.MouseMove += (_, e) =>
        {
            if (down is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(card) - start;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            down = null;
            if (!File.Exists(outPath)) return;

            _draggingOut = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { outPath });
                DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                _draggingOut = false;
            }

            if (!File.Exists(outPath))
            {
                _results.Remove(item);
                Changed?.Invoke();
            }
        };

        var menu = new ContextMenu();
        MenuItem MenuAction(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            return mi;
        }
        menu.Items.Add(MenuAction("Open", () => ShellIcons.Open(outPath)));
        menu.Items.Add(MenuAction("Show in folder", () => ShellIcons.ShowInFolder(outPath)));
        menu.Items.Add(MenuAction("Copy file", () => CopyFilesToClipboard([outPath])));
        var allOk = _results.Where(r => r.Succeeded && r.OutputPath is not null).Select(r => r.OutputPath!).ToList();
        if (allOk.Count > 1)
            menu.Items.Add(MenuAction($"Copy all ({allOk.Count} files)", () => CopyFilesToClipboard(allOk)));
        menu.Opened += (_, _) => _keepOpen(true);
        menu.Closed += (_, _) => _keepOpen(false);
        card.ContextMenu = menu;

        return card;
    }

    // ───────────────────────── Actions ─────────────────────────

    private async Task RunConvertAsync()
    {
        if (_busy || _selectedTarget is not ConvertFormat target || _inputs.Count == 0) return;

        _inputs.RemoveAll(p => !File.Exists(p));
        if (_inputs.Count == 0)
        {
            Changed?.Invoke();
            return;
        }

        _busy = true;
        _progress = 0.02;
        _statusHint = null;
        _cts = new CancellationTokenSource();
        Changed?.Invoke();

        var snapshot = _inputs.ToList();
        var options = new ConvertJobOptions(target, _quality, _mergePdf);

        try
        {
            var results = await _converter.ConvertBatchAsync(snapshot, options, (_, _, pct) =>
            {
                _dispatcher.BeginInvoke(() =>
                {
                    if (!_busy) return;
                    _progress = Math.Clamp(pct, 0.02, 1.0);
                    Changed?.Invoke();
                });
            }, _cts.Token);

            _results = results.ToList();
            var okCount = _results.Count(r => r.Succeeded);
            if (okCount > 0)
            {
                var msg = okCount == 1 && _results[0].OutputPath is string single
                    ? $"✓ Converted {Path.GetFileName(single)}"
                    : $"✓ Converted {okCount} files to {target.Label}";
                _notify(msg);
            }
        }
        catch (OperationCanceledException)
        {
            _statusHint = "Cancelled";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _busy = false;
            _progress = 0;
            Changed?.Invoke();
        }
    }

    private void BrowseFiles()
    {
        if (_busy) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose files to convert",
            Multiselect = true,
            Filter = "Supported files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.heic;*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.webm;*.mp3;*.wav;*.m4a;*.flac;*.wma;*.aac;*.ogg;*.json;*.csv;*.tsv;*.md;*.markdown|All files (*.*)|*.*",
        };
        _keepOpen(true);
        try
        {
            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
                ReceiveFiles(dlg.FileNames);
        }
        finally
        {
            _keepOpen(false);
        }
    }

    private void ClearAll()
    {
        if (_busy) return;
        _inputs.Clear();
        _results = [];
        _selectedTarget = null;
        _statusHint = null;
        Changed?.Invoke();
    }

    private static void CopyFilesToClipboard(IReadOnlyList<string> paths)
    {
        try
        {
            var col = new StringCollection();
            foreach (var p in paths)
                if (File.Exists(p)) col.Add(p);
            if (col.Count > 0) Clipboard.SetFileDropList(col);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    private static TextBlock MakeLabel(string text, double leftMargin = 0, double rightMargin = 0)
    {
        var tb = Ui.Text(text, 10.5, Ui.Dim);
        tb.Margin = new Thickness(leftMargin, 0, rightMargin, 0);
        return tb;
    }

    private static long? TryFileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : null; }
        catch (IOException) { return null; }
    }
}
