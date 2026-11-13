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
