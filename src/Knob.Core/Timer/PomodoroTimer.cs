namespace Knob.Core.Timer;

/// <summary>
/// Countdown computed from timestamps (not tick counting), so sleep/resume stays correct.
/// Callers poll <see cref="Update"/> (from a one-shot or per-second UI timer) to detect the end.
/// </summary>
public sealed class PomodoroTimer
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(180);

    private readonly IClock _clock;
    private TimeSpan _remainingAtStart;
    private DateTimeOffset _startedAt;
    private TimeSpan _pausedRemaining;

    public PomodoroTimer(IClock clock)
    {
        _clock = clock;
        Duration = TimeSpan.FromMinutes(25);
        _pausedRemaining = Duration;
    }

    public TimeSpan Duration { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>True when the countdown has moved away from a full, stopped timer (running or paused mid-way).</summary>
    public bool IsActive => IsRunning || _pausedRemaining != Duration;

    public TimeSpan Remaining
    {
        get
        {
            if (!IsRunning) return _pausedRemaining;
            var left = _remainingAtStart - (_clock.Now - _startedAt);
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    /// <summary>0 at the start, 1 at the end.</summary>
    public double Progress => Duration <= TimeSpan.Zero ? 0 : 1 - Remaining / Duration;

    public event EventHandler? Finished;

    /// <summary>Changes the duration by whole minutes (clamped to 1–180). Only while not running; resets the countdown.</summary>
    public bool AddMinutes(int minutes)
    {
        if (IsRunning) return false;
        var d = Duration + TimeSpan.FromMinutes(minutes);
        if (d < MinDuration) d = MinDuration;
        if (d > MaxDuration) d = MaxDuration;
        Duration = d;
        _pausedRemaining = d;
        return true;
    }

    public void Start()
    {
        if (IsRunning) return;
        if (_pausedRemaining <= TimeSpan.Zero) _pausedRemaining = Duration;
        _remainingAtStart = _pausedRemaining;
        _startedAt = _clock.Now;
        IsRunning = true;
    }

    public void Pause()
    {
        if (!IsRunning) return;
        _pausedRemaining = Remaining;
        IsRunning = false;
    }

    public void Reset()
    {
        IsRunning = false;
        _pausedRemaining = Duration;
    }

    /// <summary>Checks for the end. Raises <see cref="Finished"/> exactly once per run.</summary>
    public void Update()
    {
        if (!IsRunning || Remaining > TimeSpan.Zero) return;
        IsRunning = false;
        _pausedRemaining = TimeSpan.Zero;
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
