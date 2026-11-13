namespace Knob.Core;

public static class MinuteAligner
{
    /// <summary>Added so the tick lands just after the boundary, never just before it.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(20);

    public static TimeSpan DelayUntilNextMinute(DateTimeOffset now)
    {
        var intoMinute = TimeSpan.FromTicks(now.Ticks % TimeSpan.TicksPerMinute);
        return TimeSpan.FromMinutes(1) - intoMinute + Slack;
    }
}
