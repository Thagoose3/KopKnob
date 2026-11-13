namespace Knob.Core.Calendar;

public static class MonthGrid
{
    /// <summary>42 consecutive days (6 weeks) covering the month, starting on <paramref name="firstDayOfWeek"/>.</summary>
    public static IReadOnlyList<DateOnly> Build(int year, int month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var start = first.AddDays(-offset);
        var days = new DateOnly[42];
        for (var i = 0; i < days.Length; i++) days[i] = start.AddDays(i);
        return days;
    }
}
