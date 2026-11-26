using Knob.Core.Storage;

namespace Knob.Core.Tests.Color;

public class RecentColorsTests
{
    [Fact]
    public void Add_PutsNewestFirst_MovesRepeatsToFront_KeepsEight()
    {
        var t = new ToolSettings();
        foreach (var c in new[] { "#000001", "#000002", "#000003" }) t.AddColor(c);
        t.AddColor("#000001");
        Assert.Equal(["#000001", "#000003", "#000002"], t.RecentColors);

        for (var i = 10; i < 20; i++) t.AddColor($"#0000{i}");
        Assert.Equal(ToolSettings.MaxColors, t.RecentColors.Count);
        Assert.Equal("#000019", t.RecentColors[0]);
    }

    [Fact]
    public void Add_IgnoresCase_AndRejectsNonColours()
    {
        var t = new ToolSettings();
        t.AddColor("#1e90ff");
        t.AddColor("#1E90FF");
        t.AddColor("blue");
        t.AddColor("#12345");
        Assert.Equal(["#1E90FF"], t.RecentColors);
    }

    [Fact]
    public void Normalize_CleansHandEditedFile()
    {
        var t = new ToolSettings { RecentColors = ["#ABCDEF", "nope", "#abcdef", "#000000"] };
        t.Normalize();
        Assert.Equal(["#ABCDEF", "#000000"], t.RecentColors);

        var empty = new ToolSettings { RecentColors = null! };
        empty.Normalize();
        Assert.Empty(empty.RecentColors);
    }
}
