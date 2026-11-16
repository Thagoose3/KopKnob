using Knob.Core.Shell;

namespace Knob.Core.Storage;

public sealed class AppSettings : IJsonNormalizable
{
    public const string ThemeDefault = "Default";
    /// <summary>Journey-to-the-West "sutra scroll" look: lacquer red, gold rods, unrolls when it opens.</summary>
    public const string ThemeScroll = "Scroll";
    /// <summary>Dynamic-Island shape in frosted (translucent, no real blur) dark glass.</summary>
    public const string ThemeGlass = "Glass";
    public static readonly string[] Themes = [ThemeDefault, ThemeScroll, ThemeGlass];

    public string Theme { get; set; } = ThemeDefault;
    public int HoverOpenDelayMs { get; set; } = 120;
    public int CloseDelayMs { get; set; } = 350;
    public bool HideOnFullscreen { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public HotkeySettings Hotkeys { get; set; } = new();
    public int ClipboardMaxItems { get; set; } = 50;
    public DockSettings Dock { get; set; } = new();
    public TabSettings Tabs { get; set; } = new();
    public WeatherSettings Weather { get; set; } = new();

    public void Normalize()
    {
        Theme = Themes.FirstOrDefault(t => string.Equals(t, Theme, StringComparison.OrdinalIgnoreCase)) ?? ThemeDefault;
        Tabs ??= new TabSettings();
        Tabs.Normalize(TabSettings.DefaultOrder);
        Hotkeys ??= new HotkeySettings();
        Hotkeys.Normalize();
        Dock ??= new DockSettings();
        Dock.Normalize();
        Weather ??= new WeatherSettings();
        Weather.Normalize();
        if (HoverOpenDelayMs < 0) HoverOpenDelayMs = 120;
        if (CloseDelayMs < 0) CloseDelayMs = 350;
        if (ClipboardMaxItems <= 0) ClipboardMaxItems = 50;
    }
}

/// <summary>The place the weather page shows (chosen by searching a city name). Null until the user picks one.</summary>
public sealed class WeatherSettings
{
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Temperature next to the clock on the pill.</summary>
    public bool ShowOnPill { get; set; } = true;

    public bool HasPlace => !string.IsNullOrWhiteSpace(City) && Latitude is not null && Longitude is not null;

    public void Normalize()
    {
        if (Latitude is not (>= -90 and <= 90) || Longitude is not (>= -180 and <= 180) || string.IsNullOrWhiteSpace(City))
        {
            City = null;
            Latitude = Longitude = null;
        }
    }
}

public sealed class HotkeySettings
{
    public string Calculator { get; set; } = "Ctrl+Alt+K";
    public string Qr { get; set; } = "Ctrl+Alt+Q";
    public string TextCapture { get; set; } = "Ctrl+Alt+T";

    internal void Normalize()
    {
        Calculator ??= "Ctrl+Alt+K";
        Qr ??= "Ctrl+Alt+Q";
        TextCapture ??= "Ctrl+Alt+T";
    }
}
