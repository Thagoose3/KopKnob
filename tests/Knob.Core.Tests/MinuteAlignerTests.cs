namespace Knob.Core.Tests;

public class MinuteAlignerTests
{
    private static DateTimeOffset At(int h, int m, int s, int ms = 0) =>
        new(2026, 11, 13, h, m, s, ms, TimeSpan.FromHours(7));

    [Fact]
    public void MidMinute_WaitsUntilNextBoundary()
    {
        var d = MinuteAligner.DelayUntilNextMinute(At(10, 30, 15, 500));
        Assert.Equal(TimeSpan.FromMilliseconds(44_500) + MinuteAligner.Slack, d);
    }

    [Fact]
    public void ExactlyOnBoundary_WaitsFullMinute()
    {
        var d = MinuteAligner.DelayUntilNextMinute(At(10, 30, 0));
        Assert.Equal(TimeSpan.FromMinutes(1) + MinuteAligner.Slack, d);
    }

    [Fact]
    public void LastMillisecond_WaitsAlmostNothing()
    {
        var d = MinuteAligner.DelayUntilNextMinute(At(23, 59, 59, 999));
        Assert.Equal(TimeSpan.FromMilliseconds(1) + MinuteAligner.Slack, d);
    }
}
