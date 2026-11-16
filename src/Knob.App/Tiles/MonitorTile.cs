using System.Windows;
using System.Windows.Controls;
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
/// CPU / RAM / network. Polls once per second only while its page is on screen in the open panel.
/// </summary>
public sealed class MonitorTile : TileBase
{
    private const int HistoryLength = 60;

    private readonly IClock _clock;
    private readonly DispatcherTimer _poll;
    private readonly RateCalculator _down = new(), _up = new();
    private readonly Queue<double> _cpuHistory = new();
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _havePrev;
    private bool _tileVisible;
    private CpuUtilityCounter? _utility;
    private bool _triedUtility;
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
    public double Down { get; private set; }
    public double Up { get; private set; }

    public override string Id => "monitor";
    public override string Title => "System";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L, TileSize.W];

    public override void OnPanelOpened() { _tileVisible = true; SyncPolling(); }
    public override void OnPanelClosed() { _tileVisible = false; SyncPolling(); }

    private void SyncPolling()
    {
        var want = _tileVisible;
        if (want && !_poll.IsEnabled)
        {
            if (!_triedUtility)
            {
                _triedUtility = true;
                _utility = CpuUtilityCounter.TryCreate();
            }
            else
            {
                _utility?.Prime();
            }
            _havePrev = false;
            _down.Reset();
            _up.Reset();
            Sample();
            _poll.Start();
        }
        else if (!want && _poll.IsEnabled)
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
            _cpuHistory.Enqueue(c);
            while (_cpuHistory.Count > HistoryLength) _cpuHistory.Dequeue();
        }

        (RamPercent, RamUsed, RamTotal) = SystemMetrics.Memory();

        var now = _clock.Now;
        var (rx, tx) = SystemMetrics.NetworkTotals();
        Down = _down.Update(rx, now);
        Up = _up.Update(tx, now);

        Sampled?.Invoke();
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        var cpu = Ui.Text("—", 20, weight: FontWeights.SemiBold);
        var ram = Ui.Text("", 11);
        var net = Ui.Text("", 11, Ui.Subtle);

        var stats = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 104 };
        stats.Children.Add(Ui.Title("CPU"));
        stats.Children.Add(cpu);
        stats.Children.Add(ram);
        stats.Children.Add(net);

        var graphArea = new Grid { ClipToBounds = true };
        graphArea.Children.Add(new Border { Background = Ui.Control, CornerRadius = new CornerRadius(8), Opacity = 0.5 });
        var line = new Polyline { Stroke = Ui.Accent, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
        graphArea.Children.Add(line);

        var root = new DockPanel();
        DockPanel.SetDock(stats, Dock.Left);
        root.Children.Add(stats);
        root.Children.Add(graphArea);

        void Refresh()
        {
            cpu.Text = $"{Cpu:0}%";
            ram.Text = RamTotal == 0 ? "RAM —" : $"RAM {RamPercent}%  ({ByteFormatter.Format((long)RamUsed)})";
            net.Text = $"↓ {RateCalculator.Format(Down)}   ↑ {RateCalculator.Format(Up)}";

            var w = graphArea.ActualWidth;
            var h = graphArea.ActualHeight;
            var points = new PointCollection();
            if (w > 0 && h > 0)
            {
                var step = w / (HistoryLength - 1);
                var i = HistoryLength - _cpuHistory.Count;
                foreach (var v in _cpuHistory)
                {
                    points.Add(new Point(i * step, h - 2 - (h - 4) * v / 100.0));
                    i++;
                }
            }
            line.Points = points;
        }

        graphArea.SizeChanged += (_, _) => Refresh();
        Ui.WhileLoaded(root, () => { Sampled += Refresh; Refresh(); }, () => Sampled -= Refresh);
        return root;
    }
}
