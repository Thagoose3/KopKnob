using Knob.Core.Calc;

namespace Knob.Core.Tests.Calc;

public class CalcHistoryTests
{
    [Fact]
    public void NewestFirst_CappedAt20_NoRepeatOfTop()
    {
        var h = new CalcHistory();
        for (var i = 0; i < 25; i++) h.Add($"{i}+0", $"{i}");
        h.Add("24+0", "24");
        Assert.Equal(20, h.Entries.Count);
        Assert.Equal("24+0", h.Entries[0].Expression);
        Assert.Equal("5+0", h.Entries[^1].Expression);
    }

    [Fact]
    public void Normalize_DropsBrokenEntries()
    {
        var h = new CalcHistory { Entries = [null!, new CalcEntry { Expression = "", Result = "1" }, new CalcEntry { Expression = "1+1", Result = "2" }] };
        h.Normalize();
        Assert.Single(h.Entries);
        var n = new CalcHistory { Entries = null! };
        n.Normalize();
        Assert.NotNull(n.Entries);
    }

    [Theory]
    [InlineData("0.5000", "0.5")]
    [InlineData("1234567.000", "1,234,567")]
    [InlineData("-3", "-3")]
    [InlineData("0.30", "0.3")]
    public void Format(string value, string expected) =>
        Assert.Equal(expected, CalcHistory.Format(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
}
