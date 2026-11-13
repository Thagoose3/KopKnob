using System.Text.Json.Serialization;

namespace Knob.Core.Storage;

/// <summary>Where the pill sits along the top edge of the primary monitor.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotchPosition>))]
public enum NotchPosition { Left, Center, Right }

public sealed class AppSettings : IJsonNormalizable
{
    public NotchPosition Position { get; set; } = NotchPosition.Center;
    public int HoverOpenDelayMs { get; set; } = 120;
    public int CloseDelayMs { get; set; } = 350;
    public bool HideOnFullscreen { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public HotkeySettings Hotkeys { get; set; } = new();
    public int ClipboardMaxItems { get; set; } = 50;

    public void Normalize()
    {
        Hotkeys ??= new HotkeySettings();
        Hotkeys.Normalize();
        if (HoverOpenDelayMs < 0) HoverOpenDelayMs = 120;
        if (CloseDelayMs < 0) CloseDelayMs = 350;
        if (ClipboardMaxItems <= 0) ClipboardMaxItems = 50;
        if (!Enum.IsDefined(Position)) Position = NotchPosition.Center;
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
