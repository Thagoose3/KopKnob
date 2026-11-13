using System.Text.Json.Serialization;

namespace Knob.Core.Layout;

/// <summary>S = 1×1, W = 2×1, L = 2×2 (cols × rows).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TileSize>))]
public enum TileSize { S, W, L }

public static class TileSizes
{
    public static (int Cols, int Rows) Span(TileSize size) => size switch
    {
        TileSize.S => (1, 1),
        TileSize.W => (2, 1),
        TileSize.L => (2, 2),
        _ => (1, 1),
    };
}
