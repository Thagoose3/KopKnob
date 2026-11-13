using Knob.Core.Timer;

namespace Knob.Core.Tests.Timer;

public class PomodoroTimerTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void Defaults_To25Minutes_NotRunning()
    {
        var t = new PomodoroTimer(_clock);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Duration);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining);
        Assert.False(t.IsRunning);
    }

    [Fact]
    public void Start_ThenTenMinutes_Leaves15()
    {
        var t = new PomodoroTimer(_clock);
        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining);
    }

    [Fact]
    public void Pause_StopsTheCountdown_AndStartResumes()
    {
        var t = new PomodoroTimer(_clock);
        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(5));
        t.Pause();
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(20), t.Remaining);
        Assert.False(t.IsRunning);

        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(19), t.Remaining);
    }

    [Fact]
    public void PastTheEnd_FinishedFiresOnce_AndStops()
    {
        var t = new PomodoroTimer(_clock);
        var fired = 0;
        t.Finished += (_, _) => fired++;
        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(40)); // e.g. machine slept

        t.Update();
        t.Update();

        Assert.Equal(1, fired);
        Assert.False(t.IsRunning);
        Assert.Equal(TimeSpan.Zero, t.Remaining);
    }

    [Fact]
    public void Update_BeforeEnd_DoesNotFire()
    {
        var t = new PomodoroTimer(_clock);
        var fired = 0;
        t.Finished += (_, _) => fired++;
        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(24));
        t.Update();
        Assert.Equal(0, fired);
        Assert.True(t.IsRunning);
    }

    [Fact]
    public void AddMinutes_ClampsTo1And180()
    {
        var t = new PomodoroTimer(_clock);
        t.AddMinutes(-100);
        Assert.Equal(TimeSpan.FromMinutes(1), t.Duration);
        Assert.Equal(TimeSpan.FromMinutes(1), t.Remaining);
        t.AddMinutes(1000);
        Assert.Equal(TimeSpan.FromMinutes(180), t.Duration);
        t.AddMinutes(-1);
        Assert.Equal(TimeSpan.FromMinutes(179), t.Duration);
    }

    [Fact]
    public void AddMinutes_WhileRunning_IsIgnored()
    {
        var t = new PomodoroTimer(_clock);
        t.Start();
        Assert.False(t.AddMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(25), t.Duration);
    }

    [Fact]
    public void Reset_RestoresFullDuration()
    {
        var t = new PomodoroTimer(_clock);
        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(3));
        t.Reset();
        Assert.False(t.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining);
    }

    [Fact]
    public void StartAfterFinish_RunsFullDurationAgain()
    {
        var t = new PomodoroTimer(_clock);
        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(26));
        t.Update();
        t.Start();
        Assert.True(t.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining);
    }

    [Fact]
    public void Progress_GoesFromZeroToOne()
    {
        var t = new PomodoroTimer(_clock);
        Assert.Equal(0, t.Progress, 3);
        t.Start();
        _clock.Advance(TimeSpan.FromMinutes(12.5));
        Assert.Equal(0.5, t.Progress, 3);
    }
}
