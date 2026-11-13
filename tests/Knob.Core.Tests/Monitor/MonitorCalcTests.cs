using Knob.Core.Monitor;

namespace Knob.Core.Tests.Monitor;

public class CpuUsageCalculatorTests
{
    [Fact]
    public void HalfBusy_Is50Percent()
    {
        // kernel time includes idle time: kernel 60 (of which idle 50), user 40 → total 100, idle 50
        Assert.Equal(50, CpuUsageCalculator.Compute(0, 0, 0, 50, 60, 40), 3);
    }

    [Fact]
    public void AllIdle_IsZero() => Assert.Equal(0, CpuUsageCalculator.Compute(100, 200, 50, 200, 300, 50), 3);

    [Fact]
    public void FullyBusy_Is100() => Assert.Equal(100, CpuUsageCalculator.Compute(0, 0, 0, 0, 30, 70), 3);

    [Fact]
    public void NoTimePassed_IsZero() => Assert.Equal(0, CpuUsageCalculator.Compute(5, 10, 10, 5, 10, 10), 3);

    [Fact]
    public void BrokenCounters_AreClamped()
    {
        var v = CpuUsageCalculator.Compute(0, 0, 0, 500, 100, 0); // idle > total
        Assert.InRange(v, 0, 100);
    }
}

public class RateCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstSample_IsZero() => Assert.Equal(0, new RateCalculator().Update(1000, T0));

    [Fact]
    public void BytesPerSecond_FromDelta()
    {
        var r = new RateCalculator();
        r.Update(1000, T0);
        Assert.Equal(1000, r.Update(3000, T0.AddSeconds(2)), 3);
    }

    [Fact]
    public void CounterGoingBackwards_IsZero()
    {
        var r = new RateCalculator();
        r.Update(5000, T0);
        Assert.Equal(0, r.Update(100, T0.AddSeconds(1)));
        Assert.Equal(900, r.Update(1000, T0.AddSeconds(2)), 3); // recovers from the new baseline
    }

    [Fact]
    public void NoElapsedTime_IsZero()
    {
        var r = new RateCalculator();
        r.Update(0, T0);
        Assert.Equal(0, r.Update(500, T0));
    }

    [Fact]
    public void Reset_ForgetsBaseline()
    {
        var r = new RateCalculator();
        r.Update(0, T0);
        r.Reset();
        Assert.Equal(0, r.Update(10_000, T0.AddSeconds(1)));
    }
}

public class RateFormatterTests
{
    [Theory]
    [InlineData(0.0, "0 B/s")]
    [InlineData(800.0, "800 B/s")]
    [InlineData(1536.0, "1.5 KB/s")]
    [InlineData(12_582_912.0, "12 MB/s")]
    public void Format(double bps, string expected) => Assert.Equal(expected, RateCalculator.Format(bps));
}
