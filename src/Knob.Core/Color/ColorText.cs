namespace Knob.Core.Color;

/// <summary>Colour codes for the screen colour picker.</summary>
public static class ColorText
{
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static string Rgb(byte r, byte g, byte b) => $"rgb({r}, {g}, {b})";
}
