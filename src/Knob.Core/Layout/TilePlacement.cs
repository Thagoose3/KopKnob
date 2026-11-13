namespace Knob.Core.Layout;

public record TilePlacement(string TileId, int Col, int Row, TileSize Size);

/// <summary>Shape of layout.json.</summary>
public sealed class LayoutData
{
    /// <summary>null = never saved → default layout.</summary>
    public List<TilePlacement>? Placements { get; set; }
}
