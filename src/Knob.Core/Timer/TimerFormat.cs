namespace Knob.Core.Timer;

/// <summary>Countdown text. Always rounds up so "0:00" only appears when the time is really up.</summary>
public static class TimerFormat
{
    private static long CeilSeconds(TimeSpan t) =>
        t <= TimeSpan.Zero ? 0 : (long)Math.Ceiling(t.TotalSeconds - 1e-9);

    /// <summary>"m:ss" (minutes not zero-padded, can exceed 59).</summary>
    public static string Clock(TimeSpan remaining)
    {
        var s = CeilSeconds(remaining);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Compact text for the pill: "25m" for a minute or more, "0:42" in the last minute.</summary>
    public static string Pill(TimeSpan remaining)
    {
        var s = CeilSeconds(remaining);
        return s >= 60 ? $"{(s + 59) / 60}m" : $"0:{s:00}";
    }

    /// <summary>Time until the rounded-up seconds value changes.</summary>
    public static TimeSpan UntilNextSecond(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return TimeSpan.FromSeconds(1);
        var frac = TimeSpan.FromTicks(remaining.Ticks % TimeSpan.TicksPerSecond);
        return frac == TimeSpan.Zero ? TimeSpan.FromSeconds(1) : frac;
    }
}
