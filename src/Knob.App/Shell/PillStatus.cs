using System.Windows.Threading;
using Knob.App.Platform;
using Knob.Core;
using Microsoft.Win32;

namespace Knob.App.Shell;

/// <summary>
/// Drives the pill's clock and battery text. Owns the only timer allowed while collapsed:
/// one tick per minute, re-aligned to the minute boundary on every tick, after resume and on clock change.
/// Battery is read on start, on PowerModeChanged and on each minute tick — no extra timer.
/// </summary>
public sealed class PillStatus : IDisposable
{
    private readonly NotchWindow _window;
    private readonly IClock _clock;
    private readonly DispatcherTimer _minuteTimer;

    public PillStatus(NotchWindow window, IClock clock)
    {
        _window = window;
        _clock = clock;
        _minuteTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
        _minuteTimer.Tick += (_, _) => { Refresh(); ScheduleNext(); MinuteTick?.Invoke(); };
    }

    /// <summary>Raised once per minute (for later tiles that want a cheap pill update, e.g. Pomodoro).</summary>
    public event Action? MinuteTick;

    public void Start()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        Refresh();
        ScheduleNext();
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        _minuteTimer.Stop();
    }

    private void Refresh()
    {
        _window.SetTime(_clock.Now.ToString("HH:mm"));
        _window.SetBattery(PowerStatus.BatteryPercent());
    }

    private void ScheduleNext()
    {
        _minuteTimer.Stop();
        _minuteTimer.Interval = MinuteAligner.DelayUntilNextMinute(_clock.Now);
        _minuteTimer.Start();
    }

    // SystemEvents fire on their own thread: marshal to UI.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) =>
        _window.Dispatcher.BeginInvoke(() =>
        {
            Refresh();
            if (e.Mode == PowerModes.Resume) ScheduleNext();
        });

    private void OnTimeChanged(object? sender, EventArgs e) =>
        _window.Dispatcher.BeginInvoke(() => { Refresh(); ScheduleNext(); });
}
