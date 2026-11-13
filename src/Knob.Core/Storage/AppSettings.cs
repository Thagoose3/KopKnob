using Knob.Core.Shell;

namespace Knob.Core.Storage;

public sealed class AppSettings : IJsonNormalizable
{
    public int HoverOpenDelayMs { get; set; } = 120;
    public int CloseDelayMs { get; set; } = 350;
    public bool HideOnFullscreen { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public HotkeySettings Hotkeys { get; set; } = new();
    public int ClipboardMaxItems { get; set; } = 50;
    public DockSettings Dock { get; set; } = new();

    public void Normalize()
    {
        Hotkeys ??= new HotkeySettings();
        Hotkeys.Normalize();
        Dock ??= new DockSettings();
        Dock.Normalize();
        if (HoverOpenDelayMs < 0) HoverOpenDelayMs = 120;
        if (CloseDelayMs < 0) CloseDelayMs = 350;
        if (ClipboardMaxItems <= 0) ClipboardMaxItems = 50;
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
