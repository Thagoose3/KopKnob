using Knob.Core.Shell;
using Knob.Core.Storage;

namespace Knob.Core.Tests.Shell;

public class DockGeometryTests
{
    // 1536 × 864 DIP monitor at the origin
    private static DockEdge? Snap(double x, double y) => DockGeometry.Snap(x, y, 0, 0, 1536, 864);

    [Theory]
    [InlineData(0, 400, DockEdge.Left)]
    [InlineData(20, 400, DockEdge.Left)]
    [InlineData(1535, 400, DockEdge.Right)]
    [InlineData(1510, 400, DockEdge.Right)]
    [InlineData(700, 0, DockEdge.Top)]
    [InlineData(700, 25, DockEdge.Top)]
    public void NearAnEdge_Snaps(double x, double y, DockEdge expected) => Assert.Equal(expected, Snap(x, y));

    [Theory]
    [InlineData(700, 400)]
    [InlineData(100, 300)]
    [InlineData(700, 863)] // bottom edge is not a dock
    public void AwayFromEdges_Floats(double x, double y) => Assert.Null(Snap(x, y));

    [Fact]
    public void Corner_PicksTheCloserEdge()
    {
        Assert.Equal(DockEdge.Left, Snap(2, 20));
        Assert.Equal(DockEdge.Top, Snap(20, 2));
    }

    [Fact]
    public void OutsideTheMonitor_DoesNotSnap()
    {
        Assert.Null(Snap(-50, 400));   // cursor on a monitor to the left
        Assert.Null(Snap(1600, 400));
        Assert.Null(Snap(700, -20));
    }

    [Theory]
    [InlineData(768, 720, 1536, 408)]   // centred
    [InlineData(100, 720, 1536, 0)]     // clamped to the left edge
    [InlineData(1500, 720, 1536, 816)]  // clamped to the right edge
    [InlineData(50, 720, 500, 0)]       // container smaller than item
    public void Start_CentresAndClamps(double centre, double length, double container, double expected) =>
        Assert.Equal(expected, DockGeometry.Start(centre, length, container));
}

public class DockSettingsTests
{
    [Fact]
    public void Defaults_TopCentred()
    {
        var s = new AppSettings();
        Assert.Equal(DockEdge.Top, s.Dock.Edge);
        Assert.Null(s.Dock.Offset);
    }

    [Fact]
    public void RoundTrip_AsNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KnobTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore<AppSettings>(Path.Combine(dir, "settings.json"));
            var s = new AppSettings();
            s.Dock.Edge = DockEdge.Floating;
            s.Dock.FloatLeft = 300;
            s.Dock.FloatTop = 200;
            s.Dock.LastEdge = DockEdge.Right;
            s.Dock.LastOffset = 420;
            store.Save(s);

            Assert.Contains("\"Floating\"", File.ReadAllText(Path.Combine(dir, "settings.json")));
            var d = store.Load().Dock;
            Assert.Equal(DockEdge.Floating, d.Edge);
            Assert.Equal(300, d.FloatLeft);
            Assert.Equal(DockEdge.Right, d.LastEdge);
            Assert.Equal(420, d.LastOffset);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Normalize_RepairsBadValues()
    {
        var s = new AppSettings { Dock = null! };
        s.Normalize();
        Assert.NotNull(s.Dock);

        s.Dock = new DockSettings { Edge = (DockEdge)9, Offset = double.NaN, FloatLeft = double.PositiveInfinity, LastEdge = DockEdge.Floating };
        s.Normalize();
        Assert.Equal(DockEdge.Top, s.Dock.Edge);
        Assert.Null(s.Dock.Offset);
        Assert.Equal(0, s.Dock.FloatLeft);
        Assert.Equal(DockEdge.Top, s.Dock.LastEdge); // "return to" must be a real edge
    }
}
