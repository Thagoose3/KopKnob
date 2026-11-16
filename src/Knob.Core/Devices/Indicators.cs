using System.Globalization;

namespace Knob.Core.Devices;

/// <summary>Short pill label for a keyboard input language (low word of the HKL).</summary>
public static class KeyboardLanguage
{
    public static string? Label(int langId)
    {
        if (langId == 0) return null;
        try
        {
            var lang = CultureInfo.GetCultureInfo(langId).TwoLetterISOLanguageName;
            return lang switch
            {
                "th" => "ก ไทย",
                "en" => "A EN",
                _ => lang.ToUpperInvariant(),
            };
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Readable app name from a CapabilityAccessManager ConsentStore key name.</summary>
public static class CapabilityApps
{
    public static string Name(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "An app";
        if (key.Contains('#'))
        {
            // Desktop app: the exe path with '\' replaced by '#'
            var file = key[(key.LastIndexOf('#') + 1)..];
            var dot = file.LastIndexOf('.');
            return dot > 0 ? file[..dot] : file;
        }
        // Store app: PackageFamilyName "Publisher.Name_hash"
        var family = key.Split('_')[0];
        var lastDot = family.LastIndexOf('.');
        return lastDot >= 0 ? family[(lastDot + 1)..] : family;
    }
}

/// <summary>
/// Warn once when a value crosses a threshold (below it, or above it), per key, and again only after it has
/// recovered past <c>rearm</c> or <see cref="Reset"/> was called. E.g. a mouse battery at 15%, or a charge limit of 80%.
/// </summary>
public sealed class ThresholdAlarm(double threshold, double rearm, bool below)
{
    private readonly HashSet<string> _warned = [];

    public bool Check(string key, double value)
    {
        var inZone = below ? value <= threshold : value >= threshold;
        var recovered = below ? value >= rearm : value <= rearm;
        if (recovered) _warned.Remove(key);
        if (!inZone || _warned.Contains(key)) return false;
        _warned.Add(key);
        return true;
    }

    public void Reset(string key) => _warned.Remove(key);
}
