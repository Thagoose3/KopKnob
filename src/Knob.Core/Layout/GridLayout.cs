namespace Knob.Core.Layout;

/// <summary>8 × 3 grid of tiles. Every mutation keeps the invariant: no overlaps, nothing outside the grid.</summary>
public sealed class GridLayout
{
    public const int Columns = 8, Rows = 3;

    private readonly List<TilePlacement> _placements;

    private GridLayout(IEnumerable<TilePlacement> placements) => _placements = placements.ToList();

    public IReadOnlyList<TilePlacement> Placements => _placements;

    public static GridLayout Default() => new(
    [
        new("timer", 0, 0, TileSize.L),
        new("media", 2, 0, TileSize.L),
        new("clipboard", 4, 0, TileSize.L),
        new("calendar", 6, 0, TileSize.L),
        new("coffee", 0, 2, TileSize.S),
        new("notes", 1, 2, TileSize.W),
        new("todo", 3, 2, TileSize.W),
        new("clock", 5, 2, TileSize.S),
        new("disk", 6, 2, TileSize.S),
        new("launcher", 7, 2, TileSize.S),
    ]);

    /// <summary>Builds from saved data; missing, overlapping, duplicate or out-of-bounds data → default.</summary>
    public static GridLayout FromData(LayoutData? data)
    {
        var list = data?.Placements;
        if (list is null || list.Any(p => p is null || string.IsNullOrWhiteSpace(p.TileId)) || !IsValid(list))
            return Default();
        return new GridLayout(list);
    }

    public LayoutData ToData() => new() { Placements = _placements.ToList() };

    public bool TryMove(string tileId, int col, int row)
    {
        var i = IndexOf(tileId);
        return i >= 0 && TryReplace(i, _placements[i] with { Col = col, Row = row });
    }

    public bool TryResize(string tileId, TileSize size)
    {
        var i = IndexOf(tileId);
        return i >= 0 && TryReplace(i, _placements[i] with { Size = size });
    }

    /// <summary>Would <paramref name="tileId"/> fit at (col,row) with this size? Its own current cells count as free.</summary>
    public bool CanPlace(string tileId, int col, int row, TileSize size)
    {
        var candidate = new TilePlacement(tileId, col, row, size);
        if (!InBounds(candidate)) return false;
        foreach (var p in _placements)
            if (p.TileId != tileId && Overlaps(candidate, p)) return false;
        return true;
    }

    /// <summary>Take a tile off the grid (it becomes "hidden").</summary>
    public bool Hide(string tileId) => _placements.RemoveAll(p => p.TileId == tileId) > 0;

    /// <summary>Put a hidden tile on the grid at (col,row). False if it is already shown, sticks out or overlaps.</summary>
    public bool TryShow(string tileId, TileSize size, int col, int row)
    {
        if (IndexOf(tileId) >= 0 || !CanPlace(tileId, col, row, size)) return false;
        _placements.Add(new TilePlacement(tileId, col, row, size));
        return true;
    }

    /// <summary>Put a hidden tile in the first free spot (row by row, left to right). False if there is no room.</summary>
    public bool TryShowAnywhere(string tileId, TileSize size)
    {
        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Columns; c++)
                if (TryShow(tileId, size, c, r)) return true;
        return false;
    }

    public GridLayout Clone() => new(_placements);

    /// <summary>
    /// Swap two tiles (drop one onto the other). Both stay inside the cells they used together — no other tile moves —
    /// and end up in swapped reading order. Sizes adapt: the best fit fills the shared cells as fully as possible,
    /// preferring each tile to take the other's slot and size when it supports it. False if no arrangement fits.
    /// </summary>
    public bool TrySwap(string a, IReadOnlyCollection<TileSize> aSizes, string b, IReadOnlyCollection<TileSize> bSizes)
    {
        var ia = IndexOf(a);
        var ib = IndexOf(b);
        if (ia < 0 || ib < 0 || ia == ib) return false;
        var pa = _placements[ia];
        var pb = _placements[ib];

        var region = Cells(pa).Concat(Cells(pb)).ToHashSet();
        var aWasFirst = Before(pa, pb);

        TilePlacement? bestA = null, bestB = null;
        var bestScore = int.MinValue;
        foreach (var candA in Candidates(a, aSizes, region))
            foreach (var candB in Candidates(b, bSizes, region))
            {
                if (Overlaps(candA, candB)) continue;
                if (aWasFirst ? !Before(candB, candA) : !Before(candA, candB)) continue; // order must flip

                var score = 100 * (Area(candA.Size) + Area(candB.Size))
                    + (candA.Size == pb.Size ? 10 : 0) + (candB.Size == pa.Size ? 10 : 0)
                    + (candA.Col == pb.Col && candA.Row == pb.Row ? 5 : 0)
                    + (candB.Col == pa.Col && candB.Row == pa.Row ? 5 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestA = candA;
                    bestB = candB;
                }
            }

        if (bestA is null || bestB is null) return false;
        _placements[ia] = bestA;
        _placements[ib] = bestB;
        return true;
    }

    /// <summary>
    /// Drop a hidden tile onto a shown one: the hidden tile takes its spot (largest supported size that fits
    /// inside the old footprint, the same size if possible) and the shown one becomes hidden.
    /// </summary>
    public bool TryReplace(string shownId, string hiddenId, IReadOnlyCollection<TileSize> hiddenSizes)
    {
        var i = IndexOf(shownId);
        if (i < 0 || IndexOf(hiddenId) >= 0) return false;
        var old = _placements[i];
        var region = Cells(old).ToHashSet();

        var best = Candidates(hiddenId, hiddenSizes, region)
            .OrderByDescending(p => Area(p.Size))
            .ThenByDescending(p => p.Size == old.Size)
            .ThenBy(p => p.Row).ThenBy(p => p.Col)
            .FirstOrDefault();
        if (best is null) return false;
        _placements[i] = best;
        return true;
    }

    /// <summary>Every placement of <paramref name="id"/> (any of its sizes) whose cells all lie inside <paramref name="region"/>.</summary>
    private static IEnumerable<TilePlacement> Candidates(string id, IReadOnlyCollection<TileSize> sizes, HashSet<(int Col, int Row)> region)
    {
        foreach (var size in sizes.Distinct())
            foreach (var (c, r) in region)
            {
                var p = new TilePlacement(id, c, r, size);
                if (InBounds(p) && Cells(p).All(region.Contains)) yield return p;
            }
    }

    private static IEnumerable<(int Col, int Row)> Cells(TilePlacement p)
    {
        var (w, h) = TileSizes.Span(p.Size);
        for (var c = p.Col; c < p.Col + w; c++)
            for (var r = p.Row; r < p.Row + h; r++)
                yield return (c, r);
    }

    private static int Area(TileSize s)
    {
        var (w, h) = TileSizes.Span(s);
        return w * h;
    }

    /// <summary>Reading order: top row first, then left to right.</summary>
    private static bool Before(TilePlacement x, TilePlacement y) => x.Row < y.Row || (x.Row == y.Row && x.Col < y.Col);

    public static bool IsValid(IEnumerable<TilePlacement> placements)
    {
        var list = placements.ToList();
        if (list.Select(p => p.TileId).Distinct(StringComparer.Ordinal).Count() != list.Count) return false;
        var used = new bool[Columns, Rows];
        foreach (var p in list)
        {
            if (!InBounds(p)) return false;
            var (w, h) = TileSizes.Span(p.Size);
            for (var c = p.Col; c < p.Col + w; c++)
                for (var r = p.Row; r < p.Row + h; r++)
                {
                    if (used[c, r]) return false;
                    used[c, r] = true;
                }
        }
        return true;
    }

    private int IndexOf(string tileId) => _placements.FindIndex(p => p.TileId == tileId);

    private bool TryReplace(int index, TilePlacement candidate)
    {
        if (!InBounds(candidate)) return false;
        for (var j = 0; j < _placements.Count; j++)
            if (j != index && Overlaps(candidate, _placements[j])) return false;
        _placements[index] = candidate;
        return true;
    }

    private static bool InBounds(TilePlacement p)
    {
        var (w, h) = TileSizes.Span(p.Size);
        return p.Col >= 0 && p.Row >= 0 && p.Col + w <= Columns && p.Row + h <= Rows;
    }

    private static bool Overlaps(TilePlacement a, TilePlacement b)
    {
        var (aw, ah) = TileSizes.Span(a.Size);
        var (bw, bh) = TileSizes.Span(b.Size);
        return a.Col < b.Col + bw && b.Col < a.Col + aw && a.Row < b.Row + bh && b.Row < a.Row + ah;
    }
}
