using System.IO;
using Knob.Core.Storage;

namespace Knob.App.Platform;

/// <summary>
/// Tiny log of hardware events (%APPDATA%\Knob\events.log) for diagnosing alerts that didn't show.
/// Written only when an event happens; trimmed to the last ~32 KB.
/// </summary>
internal static class DiagLog
{
    private static readonly string File = Path.Combine(KnobPaths.DataDir, "events.log");
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(KnobPaths.DataDir);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > 64 * 1024)
                {
                    var text = System.IO.File.ReadAllText(File);
                    System.IO.File.WriteAllText(File, text[^(32 * 1024)..]);
                }
                System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
