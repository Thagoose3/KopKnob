namespace Knob.Core.Input;

/// <summary>Values match Win32 MOD_* flags for RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

public sealed record Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(HotkeyParser.KeyName(VirtualKey));
        return string.Join("+", parts);
    }
}

/// <summary>Parses "Ctrl+Alt+K" style strings from settings. Anything odd → null (caller skips that hotkey).</summary>
public static class HotkeyParser
{
    private static readonly Dictionary<string, HotkeyModifiers> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = HotkeyModifiers.Control,
        ["Control"] = HotkeyModifiers.Control,
        ["Alt"] = HotkeyModifiers.Alt,
        ["Shift"] = HotkeyModifiers.Shift,
        ["Win"] = HotkeyModifiers.Win,
        ["Windows"] = HotkeyModifiers.Win,
    };

    private static readonly Dictionary<string, int> Keys = BuildKeys();

    private static Dictionary<string, int> BuildKeys()
    {
        var k = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++) k[c.ToString()] = c;
        for (var d = '0'; d <= '9'; d++) k[d.ToString()] = d;
        for (var f = 1; f <= 24; f++) k[$"F{f}"] = 0x70 + f - 1;
        k["Space"] = 0x20;
        k["Tab"] = 0x09;
        k["Enter"] = 0x0D;
        k["Insert"] = 0x2D;
        k["Delete"] = 0x2E;
        k["Home"] = 0x24;
        k["End"] = 0x23;
        k["PageUp"] = 0x21;
        k["PageDown"] = 0x22;
        k["Left"] = 0x25;
        k["Up"] = 0x26;
        k["Right"] = 0x27;
        k["Down"] = 0x28;
        k["PrintScreen"] = 0x2C;
        return k;
    }

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = HotkeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0) return null;
            if (Modifiers.TryGetValue(part, out var m))
            {
                mods |= m;
            }
            else if (Keys.TryGetValue(part, out var vk))
            {
                if (key is not null) return null; // only one non-modifier key
                key = vk;
            }
            else
            {
                return null;
            }
        }
        return key is null || mods == HotkeyModifiers.None ? null : new Hotkey(mods, key.Value);
    }

    internal static string KeyName(int vk) =>
        Keys.FirstOrDefault(p => p.Value == vk).Key ?? $"0x{vk:X2}";
}
