using System.Runtime.InteropServices;
using System.Windows;

namespace Knob.App.Platform;

internal static class ClipboardUtil
{
    /// <summary>Set clipboard data, retrying while another app holds the clipboard (5 × 50 ms).</summary>
    public static async Task<bool> TrySetAsync(object data)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (COMException)
            {
                await Task.Delay(50);
            }
        }
        return false;
    }

    public static Task<bool> TrySetTextAsync(string text) => TrySetAsync(text);

    /// <summary>Clipboard text, or null (no text, or clipboard busy).</summary>
    public static string? TryGetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}
