using Knob.Core.Search;

namespace Knob.Core.Tests.Search;

public class PaletteSearchTests
{
    private sealed record Item(string Title, string Keywords = "");

    private static List<string> Rank(string query, params Item[] items) =>
        PaletteSearch.Rank(query, items, i => i.Title, i => i.Keywords).Select(i => i.Title).ToList();

    [Fact]
    public void Exact_Then_Prefix_Then_WordStart_Then_Contains()
    {
        var result = Rank("we",
            new Item("Answer"),          // contains
            new Item("New weather"),     // word start
            new Item("Weather"),         // prefix
            new Item("We"));             // exact
        Assert.Equal(["We", "Weather", "New weather", "Answer"], result);
    }

    [Fact]
    public void Fuzzy_LettersInOrder_RanksLast()
    {
        var result = Rank("clpb", new Item("Clipboard"), new Item("Calendar"));
        Assert.Equal(["Clipboard"], result);
    }

    [Fact]
    public void Keywords_Match_WhenTitleDoesNot()
    {
        var result = Rank("awake", new Item("Coffee", "keep awake sleep"), new Item("Notes"));
        Assert.Equal(["Coffee"], result);
    }

    [Fact]
    public void CaseInsensitive_AndThai()
    {
        Assert.Equal(["Weather"], Rank("WEA", new Item("Weather")));
        Assert.Equal(["โน้ตงาน"], Rank("โน้ต", new Item("โน้ตงาน"), new Item("ปฏิทิน")));
    }

    [Fact]
    public void Ties_KeepShorterTitleFirst_ThenOriginalOrder()
    {
        Assert.Equal(["Timer", "Timers b", "Timers a"], Rank("tim", new Item("Timers b"), new Item("Timers a"), new Item("Timer")));
    }

    [Fact]
    public void EmptyQuery_ReturnsNothing() => Assert.Empty(Rank("  ", new Item("Weather")));

    [Theory]
    [InlineData("12*7", true)]
    [InlineData("(2+3)/4", true)]
    [InlineData("1,250 + 20%", true)]
    [InlineData("42", false)]        // a bare number isn't a calculation
    [InlineData("weather", false)]
    [InlineData("2 +", false)]
    public void LooksLikeMath(string query, bool expected) => Assert.Equal(expected, PaletteSearch.LooksLikeMath(query));
}
