using Microsoft.Win32;

namespace Knob.App.Platform;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Knob — no admin needed.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Knob";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <returns>true if the registry now matches <paramref name="enabled"/>.</returns>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>If enabled, rewrite the path in case the exe moved.</summary>
    public static void RefreshPathIfEnabled()
    {
        if (IsEnabled()) Set(true);
    }
}
