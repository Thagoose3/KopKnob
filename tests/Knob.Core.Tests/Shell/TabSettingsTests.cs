using Knob.Core.Shell;
using Knob.Core.Storage;

namespace Knob.Core.Tests.Shell;

public class TabSettingsTests
{
    private static readonly string[] Known = ["home", "music", "timer", "notes"];

    private static TabSettings Norm(TabSettings t)
    {
        t.Normalize(Known);
        return t;
    }

    [Fact]
    public void Defaults_ShowEverything_InDefaultOrder()
    {
        var t = Norm(new TabSettings());
        Assert.Equal(Known, t.Order);
        Assert.Equal(Known, t.Visible);
        Assert.Equal("home", t.Last);
    }

    [Fact]
    public void Normalize_DropsUnknown_AppendsMissing_RemovesDuplicates()
    {
        var t = Norm(new TabSettings { Order = ["music", "bogus", "music", "home"], Hidden = ["bogus", "timer"] });
        Assert.Equal(["music", "home", "timer", "notes"], t.Order);
        Assert.Equal(["timer"], t.Hidden);
    }

    [Fact]
    public void Normalize_NeverHidesEverything()
    {
        var t = Norm(new TabSettings { Hidden = ["home", "music", "timer", "notes"] });
        Assert.NotEmpty(t.Visible);
    }

    [Fact]
    public void Normalize_LastMustBeVisible()
    {
        var t = Norm(new TabSettings { Last = "timer", Hidden = ["timer"] });
        Assert.Equal("home", t.Last);
        var u = Norm(new TabSettings { Last = "nope" });
        Assert.Equal("home", u.Last);
    }

    [Fact]
    public void Move_SwapsWithNeighbour_StopsAtEnds()
    {
        var t = Norm(new TabSettings());
        Assert.True(t.Move("music", -1));
        Assert.Equal(["music", "home", "timer", "notes"], t.Order);
        Assert.False(t.Move("music", -1));
        Assert.False(t.Move("notes", +1));
        Assert.False(t.Move("bogus", +1));
    }

    [Fact]
    public void SetHidden_CannotHideTheLastVisibleTab()
    {
        var t = Norm(new TabSettings { Hidden = ["music", "timer"] });
        Assert.True(t.SetHidden("home", true));
        Assert.False(t.SetHidden("notes", true));   // would leave nothing
        Assert.Equal(["notes"], t.Visible);
        Assert.True(t.SetHidden("music", false));
        Assert.Equal(["music", "notes"], t.Visible);
    }

    [Fact]
    public void HidingTheCurrentTab_MovesLastToAVisibleOne()
    {
        var t = Norm(new TabSettings { Last = "music" });
        t.SetHidden("music", true);
        Assert.Equal("home", t.Last);
    }

    [Fact]
    public void AppSettings_HasTabs_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KnobTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore<AppSettings>(Path.Combine(dir, "settings.json"));
            var s = store.Load();
            Assert.Equal(TabSettings.DefaultOrder, s.Tabs.Order);
            s.Tabs.Move("calc", -1);
            s.Tabs.SetHidden("system", true);
            s.Tabs.Last = "music";
            store.Save(s);

            var back = store.Load().Tabs;
            Assert.Equal(s.Tabs.Order, back.Order);
            Assert.Equal(["system"], back.Hidden);
            Assert.Equal("music", back.Last);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
