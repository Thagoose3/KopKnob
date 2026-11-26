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
    /// <summary>Draw with the GPU. Off by default: CPU drawing avoids loading the graphics drivers (~120 MB) and waking a discrete GPU.</summary>
    public bool HardwareRendering { get; set; }
    public bool RunAtStartup { get; set; }
    public HotkeySettings Hotkeys { get; set; } = new();
    public int ClipboardMaxItems { get; set; } = 50;
    public DockSettings Dock { get; set; } = new();
    public TabSettings Tabs { get; set; } = new();
    public WeatherSettings Weather { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();

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
        Alerts ??= new AlertSettings();
        Alerts.Normalize();
        Updates ??= new UpdateSettings();
        if (HoverOpenDelayMs < 0) HoverOpenDelayMs = 120;
        if (CloseDelayMs < 0) CloseDelayMs = 350;
        if (ClipboardMaxItems <= 0) ClipboardMaxItems = 50;
    }
}

/// <summary>Update checks against the project's GitHub releases.</summary>
public sealed class UpdateSettings
{
    /// <summary>Look for a new version about once a day.</summary>
    public bool AutoCheck { get; set; } = true;
    public DateTimeOffset? LastCheck { get; set; }
    /// <summary>The newest version the pill already announced (each version is announced once).</summary>
    public string? AnnouncedVersion { get; set; }
}

/// <summary>Which pill alerts are on, and their thresholds (0 turns a battery alert off).</summary>
public sealed class AlertSettings
{
    /// <summary>Tell me when the laptop has charged to this percent (battery care). 0 = off.</summary>
    public int ChargeLimit { get; set; } = 80;
    /// <summary>Warn when a connected Bluetooth device drops to this percent. 0 = off.</summary>
    public int DeviceLowBattery { get; set; } = 15;
    /// <summary>Show TH/EN when the input language is switched.</summary>
    public bool KeyboardLanguage { get; set; } = true;
    public bool CapsLock { get; set; } = true;
    /// <summary>Green/orange dots while the camera/microphone is in use.</summary>
    public bool PrivacyDots { get; set; } = true;

    public void Normalize()
    {
        ChargeLimit = ChargeLimit is >= 50 and <= 100 ? ChargeLimit : ChargeLimit <= 0 ? 0 : 80;
        DeviceLowBattery = DeviceLowBattery is >= 1 and <= 50 ? DeviceLowBattery : DeviceLowBattery <= 0 ? 0 : 15;
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
    public string Screenshot { get; set; } = "Ctrl+Alt+S";
    public string ColorPicker { get; set; } = "Ctrl+Alt+C";
    public string Search { get; set; } = "Ctrl+Alt+F";

    internal void Normalize()
    {
        Calculator ??= "Ctrl+Alt+K";
        Qr ??= "Ctrl+Alt+Q";
        TextCapture ??= "Ctrl+Alt+T";
        Screenshot ??= "Ctrl+Alt+S";
        ColorPicker ??= "Ctrl+Alt+C";
        Search ??= "Ctrl+Alt+F";
    }
}
