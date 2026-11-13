using Knob.Core.Calendar;

namespace Knob.Core.Tests.Calendar;

public class MonthGridTests
{
    [Fact]
    public void AlwaysSixWeeks_OfConsecutiveDays()
    {
        var days = MonthGrid.Build(2026, 2, DayOfWeek.Monday);
        Assert.Equal(42, days.Count);
        for (var i = 1; i < days.Count; i++)
            Assert.Equal(days[i - 1].AddDays(1), days[i]);
    }

    [Fact]
    public void SundayStart_MonthStartingOnSunday_StartsOnThe1st()
    {
        // 1 Nov 2026 is a Sunday
        var days = MonthGrid.Build(2026, 11, DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 11, 1), days[0]);
    }

    [Fact]
    public void MondayStart_IncludesTailOfPreviousMonth()
    {
        var days = MonthGrid.Build(2026, 11, DayOfWeek.Monday);
        Assert.Equal(new DateOnly(2026, 10, 26), days[0]);
        Assert.Equal(DayOfWeek.Monday, days[0].DayOfWeek);
    }

    [Fact]
    public void ContainsEveryDayOfTheMonth()
    {
        var days = MonthGrid.Build(2024, 2, DayOfWeek.Sunday);
        Assert.Equal(29, days.Count(d => d.Month == 2));
    }
}
