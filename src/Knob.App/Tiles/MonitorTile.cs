using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.Core;
using Knob.Core.Layout;
using Knob.Core.Monitor;
using Knob.Core.Storage;

namespace Knob.App.Tiles;

/// <summary>
/// CPU / RAM / GPU / network. Polls once per second only while its page is on screen in the open panel.
/// CPU and GPU use the same PDH counters as Task Manager.
/// </summary>
public sealed class MonitorTile : TileBase
{
    private const int HistoryLength = 60;

    private static readonly Color Teal = Color.FromRgb(0x14, 0xB8, 0xA6);
    private static readonly Color Pink = Color.FromRgb(0xEC, 0x48, 0x99);
    private static readonly Color Amber = Color.FromRgb(0xF5, 0x9E, 0x0B);

    private readonly IClock _clock;
    private readonly DispatcherTimer _poll;
    private readonly RateCalculator _down = new(), _up = new();
    private readonly History _cpuHistory = new(), _ramHistory = new(), _gpuHistory = new(), _downHistory = new(), _upHistory = new();
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _havePrev;
    private bool _visible;
    private CpuUtilityCounter? _utility;
    private GpuCounters? _gpu;
    private bool _triedCounters;
    private string? _gpuName;
    private ulong _gpuTotal;
    private string? _cpuName;
    private event Action? Sampled;

    public MonitorTile(IClock clock, Dispatcher dispatcher)
    {
        _clock = clock;
        _poll = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => Sample();
    }

    public double Cpu { get; private set; }
    public int RamPercent { get; private set; }
    public ulong RamUsed { get; private set; }
    public ulong RamTotal { get; private set; }
    public double? Gpu { get; private set; }
    public double? GpuMemory { get; private set; }
    public double Down { get; private set; }
    public double Up { get; private set; }

    public override string Id => "monitor";
    public override string Title => "System";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L, TileSize.W];

    public override void OnPanelOpened() { _visible = true; SyncPolling(); }
    public override void OnPanelClosed() { _visible = false; SyncPolling(); }

    private void SyncPolling()
    {
        if (_visible && !_poll.IsEnabled)
        {
            if (!_triedCounters)
            {
                _triedCounters = true;
                _utility = CpuUtilityCounter.TryCreate();
                _gpu = GpuCounters.TryCreate();
                (_gpuName, _gpuTotal) = GpuCounters.ReadAdapterInfo();
                _cpuName = ReadCpuName();
            }
            else
            {
                _utility?.Prime();
                _gpu?.Prime();
            }
            _havePrev = false;
            _down.Reset();
            _up.Reset();
            Sample();
            _poll.Start();
        }
        else if (!_visible && _poll.IsEnabled)
        {
            _poll.Stop();
        }
    }

    private void Sample()
    {
        // Prefer Task Manager's frequency-aware "% Processor Utility"; fall back to time-based GetSystemTimes.
        double? cpu = null;
        if (SystemMetrics.TryGetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (_havePrev) cpu = CpuUsageCalculator.Compute(_prevIdle, _prevKernel, _prevUser, idle, kernel, user);
            (_prevIdle, _prevKernel, _prevUser, _havePrev) = (idle, kernel, user, true);
        }
        if (_utility?.Sample() is double u && cpu is not null) cpu = u;
        if (cpu is double c)
        {
            Cpu = c;
            _cpuHistory.Add(c);
        }

        (RamPercent, RamUsed, RamTotal) = SystemMetrics.Memory();
        _ramHistory.Add(RamPercent);

        if (_gpu is not null)
        {
            var (load, mem) = _gpu.Sample();
            Gpu = load;
            GpuMemory = mem;
            if (load is double l) _gpuHistory.Add(l);
        }

        var now = _clock.Now;
        var (rx, tx) = SystemMetrics.NetworkTotals();
        Down = _down.Update(rx, now);
        Up = _up.Update(tx, now);
        _downHistory.Add(Down);
        _upHistory.Add(Up);

        Sampled?.Invoke();
    }

    /// <summary>System page: four cards — CPU, RAM, GPU, Network — each with a big number and a 60-second graph.</summary>
    public override FrameworkElement CreateView(TileSize size)
    {
        var cpu = new MetricCard("CPU", Ui.Res<SolidColorBrush>("AccentBrush").Color);
        var ram = new MetricCard("Memory", Teal);
        var gpu = new MetricCard("GPU", Pink);
        var net = new MetricCard("Network", Amber, secondLine: Color.FromRgb(0x9A, 0x9A, 0xA5));

        var grid = new UniformGrid { Columns = 4 };
        foreach (var card in new[] { cpu, ram, gpu, net }) grid.Children.Add(card.Root);
        var root = Pages.Smooth(new Border { Padding = new Thickness(4, 0, 4, 0), Child = grid });

        void Refresh()
        {
            cpu.Set($"{Cpu:0}%", Short(_cpuName) ?? "Processor", _cpuHistory, 100);

            var ramText = RamTotal == 0 ? "—" : $"{ByteFormatter.Format((long)RamUsed)} of {ByteFormatter.Format((long)RamTotal)}";
            ram.Set(RamTotal == 0 ? "—" : $"{RamPercent}%", ramText, _ramHistory, 100);

            var vram = GpuMemory is double m
                ? _gpuTotal > 0 ? $"VRAM {ByteFormatter.Format((long)m)} of {ByteFormatter.Format((long)_gpuTotal)}" : $"VRAM {ByteFormatter.Format((long)m)}"
                : "";
            gpu.Set(Gpu is double g ? $"{g:0}%" : "—", Short(_gpuName) ?? "Not available", _gpuHistory, 100, vram);

            var peak = Math.Max(1024, Math.Max(_downHistory.Max, _upHistory.Max));
            net.Set($"↓ {RateCalculator.Format(Down)}", $"↑ {RateCalculator.Format(Up)}", _downHistory, peak, second: _upHistory, bigSize: 17);
        }

        Ui.WhileLoaded(root, () => { Sampled += Refresh; Refresh(); }, () => Sampled -= Refresh);
        return root;
    }

    /// <summary>"NVIDIA GeForce RTX 5050 Laptop GPU" → "GeForce RTX 5050 Laptop GPU"; trims vendor/marketing noise.</summary>
    private static string? Short(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var s = name.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace("CPU", "").Replace("NVIDIA ", "")
            .Replace("Processor", "").Replace("  ", " ").Trim();
        var at = s.IndexOf(" @", StringComparison.Ordinal);
        return at > 0 ? s[..at].Trim() : s;
    }

    private static string? ReadCpuName()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return k?.GetValue("ProcessorNameString") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Last 60 samples.</summary>
    private sealed class History
    {
        private readonly Queue<double> _values = new();
        public IEnumerable<double> Values => _values;
        public int Count => _values.Count;
        public double Max => _values.Count == 0 ? 0 : _values.Max();

        public void Add(double v)
        {
            _values.Enqueue(v);
            while (_values.Count > HistoryLength) _values.Dequeue();
        }
    }

    /// <summary>One card: label, big value, detail lines, filled line graph (optionally a second line).</summary>
    private sealed class MetricCard
    {
        private readonly TextBlock _value, _detail, _extra;
        private readonly Grid _graph;
        private readonly Polygon _area;
        private readonly Polyline _line, _line2;

        public MetricCard(string label, Color color, Color? secondLine = null)
        {
            var title = Ui.Text(label.ToUpperInvariant(), 10, Ui.Subtle, FontWeights.SemiBold);
            _value = Ui.Text("—", 24, Ui.Fg, FontWeights.SemiBold);
            _value.Margin = new Thickness(0, 2, 0, 0);
            _detail = new TextBlock { FontSize = 10.5, Foreground = Ui.Subtle, TextTrimming = TextTrimming.CharacterEllipsis };
            _extra = new TextBlock { FontSize = 10.5, Foreground = Ui.Subtle, TextTrimming = TextTrimming.CharacterEllipsis };

            var top = new StackPanel();
            top.Children.Add(title);
            top.Children.Add(_value);
            top.Children.Add(_detail);
            top.Children.Add(_extra);

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var fill = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B));
            fill.Freeze();
            _area = new Polygon { Fill = fill };
            _line = new Polyline { Stroke = brush, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            var c2 = secondLine ?? color;
            _line2 = new Polyline { Stroke = new SolidColorBrush(c2), StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.8 };
            _graph = new Grid { ClipToBounds = true, Margin = new Thickness(0, 6, 0, 0), MinHeight = 34 };
            _graph.Children.Add(_area);
            _graph.Children.Add(_line);
            _graph.Children.Add(_line2);

            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            dock.Children.Add(_graph);
            Root = Pages.Card(dock, new Thickness(4));
            Root.Padding = new Thickness(12, 10, 12, 10);
        }

        public Border Root { get; }

        public void Set(string value, string detail, History history, double max, string extra = "", History? second = null, double bigSize = 24)
        {
            _value.Text = value;
            _value.FontSize = bigSize;
            _detail.Text = detail;
            _detail.ToolTip = detail;
            _extra.Text = extra;
            _extra.Visibility = string.IsNullOrEmpty(extra) ? Visibility.Collapsed : Visibility.Visible;

            var (line, area) = Points(history, max);
            _line.Points = line;
            _area.Points = area;
            _line2.Points = second is null ? new PointCollection() : Points(second, max).Line;
        }

        private (PointCollection Line, PointCollection Area) Points(History h, double max)
        {
            var w = _graph.ActualWidth;
            var ht = _graph.ActualHeight;
            var line = new PointCollection();
            var area = new PointCollection();
            if (w <= 0 || ht <= 0 || h.Count == 0 || max <= 0) return (line, area);

            var step = w / (HistoryLength - 1);
            var x = (HistoryLength - h.Count) * step;
            area.Add(new Point(x, ht));
            foreach (var v in h.Values)
            {
                var p = new Point(x, ht - 1 - (ht - 3) * Math.Clamp(v / max, 0, 1));
                line.Add(p);
                area.Add(p);
                x += step;
            }
            area.Add(new Point(x - step, ht));
            return (line, area);
        }
    }
}
