using Knob.Core.Timer;

namespace Knob.Core.Tests.Timer;

public class TimerFormatTests
{
    [Theory]
    [InlineData(25 * 60.0, "25:00")]
    [InlineData(59.2, "1:00")]      // rounds up: never shows 0:00 while time is left
    [InlineData(42.0, "0:42")]
    [InlineData(0.0, "0:00")]
    [InlineData(180 * 60.0, "180:00")]
    public void Clock(double seconds, string expected) =>
        Assert.Equal(expected, TimerFormat.Clock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(24 * 60 + 30.0, "25m")]
    [InlineData(60.0, "1m")]
    [InlineData(59.5, "1m")]       // 60 s after rounding up → still minutes
    [InlineData(59.0, "0:59")]
    [InlineData(42.3, "0:43")]
    [InlineData(0.4, "0:01")]
    public void Pill(double seconds, string expected) =>
        Assert.Equal(expected, TimerFormat.Pill(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(42.3, 300)]  // next whole-second boundary of the countdown
    [InlineData(42.0, 1000)]
    public void UntilNextSecond(double seconds, int expectedMs) =>
        Assert.InRange(TimerFormat.UntilNextSecond(TimeSpan.FromSeconds(seconds)).TotalMilliseconds, expectedMs - 1, expectedMs + 1);
}
