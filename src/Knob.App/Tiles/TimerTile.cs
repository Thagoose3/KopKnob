using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Knob.App.Shell;
using Knob.Core;
using Knob.Core.Layout;
using Knob.Core.Timer;

namespace Knob.App.Tiles;

/// <summary>
/// Owns the Pomodoro timer and its single DispatcherTimer:
/// panel open (or last minute) → ticks on each countdown second; collapsed → one-shot to the last minute.
/// Above one minute the pill text is refreshed by the shared minute tick, not by a timer of our own.
/// </summary>
public sealed class PomodoroService
{
    private readonly NotchWindow _window;
    private readonly DispatcherTimer _tick;
    private bool _panelOpen;

    public PomodoroService(IClock clock, NotchWindow window, PillStatus pill)
    {
        _window = window;
        Timer = new PomodoroTimer(clock);
        Timer.Finished += (_, _) =>
        {
            SystemSounds.Asterisk.Play();
            _window.FlashPill();
        };
        _tick = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher);
        _tick.Tick += (_, _) => { Timer.Update(); Notify(); };
        pill.MinuteTick += () => { if (Timer.IsRunning) UpdatePill(); };
    }

    public PomodoroTimer Timer { get; }

    public event Action? Changed;

    public bool PanelOpen
    {
        get => _panelOpen;
        set { _panelOpen = value; Reschedule(); }
    }

    public void StartPause()
    {
        if (Timer.IsRunning) Timer.Pause(); else Timer.Start();
        Notify();
    }

    public void AddMinutes(int m)
    {
        if (Timer.AddMinutes(m)) Notify();
    }

    public void Reset()
    {
        Timer.Reset();
        Notify();
    }

    private void Notify()
    {
        UpdatePill();
        Changed?.Invoke();
        Reschedule();
    }

    private void UpdatePill()
    {
        var rem = Timer.Remaining;
        var show = Timer.IsRunning || (rem > TimeSpan.Zero && rem != Timer.Duration);
        _window.SetPillTimer(show ? (Timer.IsRunning ? "" : "⏸ ") + TimerFormat.Pill(rem) : null);
    }

    private void Reschedule()
    {
        _tick.Stop();
        if (!Timer.IsRunning) return;

        var rem = Timer.Remaining;
        var slack = TimeSpan.FromMilliseconds(15);
        _tick.Interval = _panelOpen || rem <= TimeSpan.FromMinutes(1)
            ? TimerFormat.UntilNextSecond(rem) + slack
            : rem - TimeSpan.FromMinutes(1) + slack;
        _tick.Start();
    }
}

public sealed class TimerTile(PomodoroService service) : TileBase
{
    private const double RingSize = 92, RingThickness = 6;

    public override string Id => "timer";
    public override string Title => "Timer";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L, TileSize.S];

    public override void OnPanelOpened() => service.PanelOpen = true;
    public override void OnPanelClosed() => service.PanelOpen = false;

    public override FrameworkElement CreateView(TileSize size) => size == TileSize.L ? LargeView() : SmallView();

    private FrameworkElement LargeView()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var ring = new Grid { Width = RingSize, Height = RingSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = Ui.Control, StrokeThickness = RingThickness, Margin = new Thickness(RingThickness / 2) });
        var arc = new Path { Stroke = Ui.Accent, StrokeThickness = RingThickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        ring.Children.Add(arc);
        var time = Ui.Text("", 20, weight: FontWeights.SemiBold);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Children.Add(time);
        root.Children.Add(ring);

        var minus = Ui.IconButton(Ui.IconRemove, (_, _) => service.AddMinutes(-1));
        var play = Ui.IconButton(Ui.IconPlay, (_, _) => service.StartPause());
        var plus = Ui.IconButton(Ui.IconAdd, (_, _) => service.AddMinutes(1));
        var reset = Ui.IconButton(Ui.IconReset, (_, _) => service.Reset());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var b in new[] { minus, play, plus, reset }) buttons.Children.Add(b);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        void Refresh()
        {
            var t = service.Timer;
            time.Text = TimerFormat.Clock(t.Remaining);
            arc.Data = Ui.Arc(t.Remaining / t.Duration, RingSize, RingThickness);
            ((TextBlock)play.Content).Text = t.IsRunning ? Ui.IconPause : Ui.IconPlay;
            minus.IsEnabled = plus.IsEnabled = !t.IsRunning;
        }

        Ui.WhileLoaded(root, () => { service.Changed += Refresh; Refresh(); }, () => service.Changed -= Refresh);
        return root;
    }

    private FrameworkElement SmallView()
    {
        var time = Ui.Text("", 16, weight: FontWeights.SemiBold);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        var play = Ui.IconButton(Ui.IconPlay, (_, _) => service.StartPause());
        play.Margin = new Thickness(0, 4, 0, 0);
        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(time);
        root.Children.Add(play);

        void Refresh()
        {
            time.Text = TimerFormat.Clock(service.Timer.Remaining);
            ((TextBlock)play.Content).Text = service.Timer.IsRunning ? Ui.IconPause : Ui.IconPlay;
        }

        Ui.WhileLoaded(root, () => { service.Changed += Refresh; Refresh(); }, () => service.Changed -= Refresh);
        return root;
    }
}
