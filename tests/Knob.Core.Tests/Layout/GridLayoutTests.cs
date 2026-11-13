using Knob.Core.Layout;
using Knob.Core.Storage;

namespace Knob.Core.Tests.Layout;

public class GridLayoutTests
{
    private static IEnumerable<(int c, int r)> Cells(TilePlacement p)
    {
        var (w, h) = TileSizes.Span(p.Size);
        for (var c = p.Col; c < p.Col + w; c++)
            for (var r = p.Row; r < p.Row + h; r++)
                yield return (c, r);
    }

    private static GridLayout Small() => GridLayout.FromData(new LayoutData
    {
        Placements =
        [
            new("a", 0, 0, TileSize.L),
            new("b", 2, 0, TileSize.S),
            new("c", 7, 2, TileSize.S),
        ],
    });

    [Fact]
    public void Default_HasNoOverlaps_AndFitsGrid()
    {
        var used = new HashSet<(int, int)>();
        foreach (var p in GridLayout.Default().Placements)
            foreach (var (c, r) in Cells(p))
            {
                Assert.InRange(c, 0, GridLayout.Columns - 1);
                Assert.InRange(r, 0, GridLayout.Rows - 1);
                Assert.True(used.Add((c, r)), $"{p.TileId} overlaps at {c},{r}");
            }
    }

    [Fact]
    public void Default_ContainsEveryPlannedTileOnce()
    {
        var ids = GridLayout.Default().Placements.Select(p => p.TileId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var id in new[] { "timer", "media", "clipboard", "calendar", "clock", "coffee", "notes", "todo", "disk", "launcher" })
            Assert.Contains(id, ids);
        Assert.DoesNotContain("monitor", ids);
    }

    [Fact]
    public void TryMove_ToFreeCell_Moves()
    {
        var g = Small();
        Assert.True(g.TryMove("b", 4, 1));
        Assert.Contains(new TilePlacement("b", 4, 1, TileSize.S), g.Placements);
    }

    [Fact]
    public void TryMove_OntoAnotherTile_ReturnsFalse_AndKeepsState()
    {
        var g = Small();
        var before = g.Placements.ToList();
        Assert.False(g.TryMove("b", 1, 1)); // inside "a"
        Assert.Equal(before, g.Placements);
    }

    [Theory]
    [InlineData("a", 7, 0)]  // L would stick out on the right
    [InlineData("a", 0, 2)]  // L would stick out at the bottom
    [InlineData("b", -1, 0)]
    [InlineData("b", 8, 0)]
    [InlineData("b", 0, 3)]
    public void TryMove_OutOfBounds_ReturnsFalse(string id, int col, int row)
    {
        var g = Small();
        var before = g.Placements.ToList();
        Assert.False(g.TryMove(id, col, row));
        Assert.Equal(before, g.Placements);
    }

    [Fact]
    public void TryMove_UnknownTile_ReturnsFalse()
    {
        Assert.False(Small().TryMove("zzz", 5, 0));
    }

    [Fact]
    public void TryResize_ToL_AtEdge_ReturnsFalse()
    {
        var g = Small();
        Assert.False(g.TryResize("c", TileSize.L));
        Assert.Contains(new TilePlacement("c", 7, 2, TileSize.S), g.Placements);
    }

    [Fact]
    public void TryResize_IntoNeighbour_ReturnsFalse()
    {
        var g = GridLayout.FromData(new LayoutData { Placements = [new("x", 0, 0, TileSize.S), new("y", 1, 0, TileSize.S)] });
        Assert.False(g.TryResize("x", TileSize.W));
    }

    [Fact]
    public void TryResize_WithRoom_Resizes()
    {
        var g = Small();
        Assert.True(g.TryResize("b", TileSize.L));
        Assert.Contains(new TilePlacement("b", 2, 0, TileSize.L), g.Placements);
    }

    [Fact]
    public void Hide_RemovesTile_AndFreesItsCells()
    {
        var g = Small();
        Assert.True(g.Hide("a"));
        Assert.DoesNotContain(g.Placements, p => p.TileId == "a");
        Assert.True(g.TryMove("b", 0, 0)); // a's old cells are free now
        Assert.False(g.Hide("a"));         // already hidden
    }

    [Fact]
    public void TryShow_AtFreeSpot_Adds()
    {
        var g = Small();
        Assert.True(g.TryShow("new", TileSize.W, 4, 1));
        Assert.Contains(new TilePlacement("new", 4, 1, TileSize.W), g.Placements);
    }

    [Fact]
    public void TryShow_Blocked_OrAlreadyShown_ReturnsFalse()
    {
        var g = Small();
        Assert.False(g.TryShow("new", TileSize.S, 1, 1));  // inside "a"
        Assert.False(g.TryShow("new", TileSize.L, 7, 0));  // sticks out
        Assert.False(g.TryShow("b", TileSize.S, 5, 0));    // b is already on the grid
    }

    [Fact]
    public void HideThenShow_WhenNoSpaceLeft_ReturnsFalse()
    {
        var g = GridLayout.Default();              // the default fills all 24 cells
        Assert.True(g.Hide("disk"));
        Assert.True(g.TryShowAnywhere("monitor", TileSize.S)); // takes disk's cell
        Assert.False(g.TryShowAnywhere("disk", TileSize.S));   // nowhere left
        Assert.DoesNotContain(g.Placements, p => p.TileId == "disk");
    }

    [Fact]
    public void TryShowAnywhere_FindsFirstFreeSpot()
    {
        var g = Small();
        Assert.True(g.TryShowAnywhere("x", TileSize.L));
        var x = g.Placements.Single(p => p.TileId == "x");
        Assert.Equal((3, 0), (x.Col, x.Row)); // first L-sized hole scanning row by row, left to right
    }

    [Fact]
    public void CanPlace_IgnoresTheTileItself()
    {
        var g = Small();
        Assert.True(g.CanPlace("a", 0, 1, TileSize.L));          // rows 1–2 are free (and overlapping itself is fine)
        Assert.False(g.CanPlace("a", 0, 2, TileSize.L));         // would stick out at the bottom
        Assert.True(g.CanPlace("a", 0, 0, TileSize.L));          // its own cells
        Assert.False(g.CanPlace("b", 1, 0, TileSize.S));         // inside a
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var g = Small();
        var copy = g.Clone();
        copy.TryMove("b", 5, 1);
        Assert.Contains(new TilePlacement("b", 2, 0, TileSize.S), g.Placements);
        Assert.Contains(new TilePlacement("b", 5, 1, TileSize.S), copy.Placements);
    }

    [Fact]
    public void FromData_EmptyList_IsValid_AllHidden()
    {
        var g = GridLayout.FromData(new LayoutData { Placements = [] });
        Assert.Empty(g.Placements);
    }

    private static readonly TileSize[] SW = [TileSize.S, TileSize.W];
    private static readonly TileSize[] WL = [TileSize.W, TileSize.L];
    private static readonly TileSize[] SL = [TileSize.S, TileSize.L];
    private static readonly TileSize[] OnlyL = [TileSize.L];

    private static TilePlacement P(GridLayout g, string id) => g.Placements.Single(p => p.TileId == id);

    [Fact]
    public void TrySwap_SameSize_ExchangesPlaces()
    {
        var g = GridLayout.Default();
        Assert.True(g.TrySwap("media", WL, "clipboard", WL));
        Assert.Equal(new TilePlacement("media", 4, 0, TileSize.L), P(g, "media"));
        Assert.Equal(new TilePlacement("clipboard", 2, 0, TileSize.L), P(g, "clipboard"));
    }

    [Fact]
    public void TrySwap_DifferentSizes_FitsTheSharedSpace_InSwappedOrder()
    {
        var g = GridLayout.Default(); // coffee (0,2,S), notes (1,2,W)
        Assert.True(g.TrySwap("coffee", SW, "notes", WL));
        Assert.Equal(new TilePlacement("notes", 0, 2, TileSize.W), P(g, "notes"));
        Assert.Equal(new TilePlacement("coffee", 2, 2, TileSize.S), P(g, "coffee"));
        Assert.True(GridLayout.IsValid(g.Placements));
    }

    [Fact]
    public void TrySwap_BigIntoSmallSlot_ShrinksToASupportedSize()
    {
        var g = GridLayout.Default(); // timer (0,0,L), clock (5,2,S)
        Assert.True(g.TrySwap("timer", SL, "clock", SW));
        Assert.Equal(new TilePlacement("timer", 5, 2, TileSize.S), P(g, "timer"));
        var clock = P(g, "clock");
        Assert.Equal((0, 0), (clock.Col, clock.Row));
        Assert.Equal(TileSize.W, clock.Size);          // largest Clock size that fits the old Timer spot
        Assert.True(GridLayout.IsValid(g.Placements));
    }

    [Fact]
    public void TrySwap_Impossible_ReturnsFalse_AndLeavesLayoutAlone()
    {
        var g = GridLayout.Default();
        var before = g.Placements.ToList();
        Assert.False(g.TrySwap("calendar", OnlyL, "coffee", SW)); // L-only can't fit one cell
        Assert.Equal(before, g.Placements);
    }

    [Fact]
    public void TrySwap_NeverMovesOtherTiles()
    {
        var g = GridLayout.Default();
        var others = g.Placements.Where(p => p.TileId is not ("timer" or "clock")).ToList();
        Assert.True(g.TrySwap("timer", SL, "clock", SW));
        Assert.All(others, o => Assert.Contains(o, g.Placements));
        // and a pair that could only work by pushing others aside is refused
        Assert.False(g.TrySwap("todo", WL, "disk", SW));
    }

    [Fact]
    public void TryReplace_HiddenTileTakesTheSpot_OldOneIsHidden()
    {
        var g = GridLayout.Default();
        Assert.True(g.TryReplace("notes", "monitor", WL));
        Assert.Equal(new TilePlacement("monitor", 1, 2, TileSize.W), P(g, "monitor"));
        Assert.DoesNotContain(g.Placements, p => p.TileId == "notes");
    }

    [Fact]
    public void TryReplace_DoesNotFit_ReturnsFalse()
    {
        var g = GridLayout.Default();
        var before = g.Placements.ToList();
        Assert.False(g.TryReplace("disk", "monitor", WL)); // W/L can't fit a single cell
        Assert.Equal(before, g.Placements);
    }

    [Fact]
    public void Serialize_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KnobTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore<LayoutData>(Path.Combine(dir, "layout.json"));
            var g = GridLayout.Default();
            g.TryResize("disk", TileSize.S);
            store.Save(g.ToData());

            var loaded = GridLayout.FromData(store.Load());

            Assert.Equal(g.Placements, loaded.Placements);
            Assert.Contains("\"L\"", File.ReadAllText(Path.Combine(dir, "layout.json"))); // sizes stored as names
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FromData_Missing_ReturnsDefault()
    {
        Assert.Equal(GridLayout.Default().Placements, GridLayout.FromData(new LayoutData()).Placements);
        Assert.Equal(GridLayout.Default().Placements, GridLayout.FromData(null).Placements);
    }

    [Fact]
    public void FromData_Overlapping_FallsBackToDefault()
    {
        var data = new LayoutData { Placements = [new("a", 0, 0, TileSize.L), new("b", 1, 1, TileSize.S)] };
        Assert.Equal(GridLayout.Default().Placements, GridLayout.FromData(data).Placements);
    }

    [Fact]
    public void FromData_DuplicateOrOutOfBounds_FallsBackToDefault()
    {
        Assert.Equal(GridLayout.Default().Placements,
            GridLayout.FromData(new LayoutData { Placements = [new("a", 0, 0, TileSize.S), new("a", 3, 0, TileSize.S)] }).Placements);
        Assert.Equal(GridLayout.Default().Placements,
            GridLayout.FromData(new LayoutData { Placements = [new("a", 7, 0, TileSize.W)] }).Placements);
    }
}
