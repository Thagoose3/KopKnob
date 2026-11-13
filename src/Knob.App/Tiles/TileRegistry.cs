namespace Knob.App.Tiles;

public sealed class TileRegistry
{
    private readonly Dictionary<string, ITile> _tiles = new(StringComparer.Ordinal);

    public TileRegistry(IEnumerable<ITile> tiles)
    {
        foreach (var t in tiles) _tiles[t.Id] = t;
    }

    public IEnumerable<ITile> All => _tiles.Values;

    public bool TryGet(string id, out ITile tile) => _tiles.TryGetValue(id, out tile!);
}
