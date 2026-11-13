using System.Text.Json.Serialization;

namespace Knob.Core.Shell;

/// <summary>Where the knob lives: on the top / left / right edge of the primary monitor, or floating anywhere.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DockEdge>))]
public enum DockEdge { Top, Left, Right, Floating }

/// <summary>Stored in settings.json under "Dock". All lengths are DIP relative to the primary monitor.</summary>
public sealed class DockSettings
{
    public DockEdge Edge { get; set; } = DockEdge.Top;

    /// <summary>Pill centre along the edge: from the monitor's left (Top) or top (Left/Right). null = centred.</summary>
    public double? Offset { get; set; }

    /// <summary>Floating window's top-left.</summary>
    public double FloatLeft { get; set; }
    public double FloatTop { get; set; }

    /// <summary>The edge to go back to when a floating panel is closed (✕ / Esc).</summary>
    public DockEdge LastEdge { get; set; } = DockEdge.Top;
    public double? LastOffset { get; set; }

    public void Normalize()
    {
        if (!Enum.IsDefined(Edge)) Edge = DockEdge.Top;
        if (!Enum.IsDefined(LastEdge) || LastEdge == DockEdge.Floating) LastEdge = DockEdge.Top;
        if (Offset is double o && !double.IsFinite(o)) Offset = null;
        if (LastOffset is double lo && !double.IsFinite(lo)) LastOffset = null;
        if (!double.IsFinite(FloatLeft)) FloatLeft = 0;
        if (!double.IsFinite(FloatTop)) FloatTop = 0;
    }
}

public static class DockGeometry
{
    /// <summary>How close (DIP) the cursor must be to an edge for a drop to dock there.</summary>
    public const double SnapDistance = 32;

    /// <summary>
    /// The edge a drop at (x, y) docks to: the closest of left / right / top within <paramref name="threshold"/>,
    /// or null to float. The cursor must be on this monitor (not on a neighbouring one).
    /// </summary>
    public static DockEdge? Snap(double x, double y, double monLeft, double monTop, double monWidth, double monHeight,
        double threshold = SnapDistance)
    {
        var insideX = x >= monLeft && x <= monLeft + monWidth;
        var insideY = y >= monTop && y <= monTop + monHeight;
        if (!insideX || !insideY) return null;

        DockEdge? best = null;
        var bestDistance = double.MaxValue;
        void Consider(DockEdge edge, double d)
        {
            if (d >= 0 && d <= threshold && d < bestDistance)
            {
                best = edge;
                bestDistance = d;
            }
        }
        Consider(DockEdge.Left, x - monLeft);
        Consider(DockEdge.Top, y - monTop);
        Consider(DockEdge.Right, monLeft + monWidth - x);
        return best;
    }

    /// <summary>Start position that centres an item of <paramref name="length"/> on <paramref name="centre"/>, kept inside the container.</summary>
    public static double Start(double centre, double length, double container) =>
        Math.Clamp(centre - length / 2, 0, Math.Max(0, container - length));
}
