using System.IO;
using Knob.Core.Storage;

namespace Knob.App.Platform;

/// <summary>Appends unexpected exceptions to %APPDATA%\Knob\crash.log (kept under ~1 MB). Never throws.</summary>
internal static class CrashLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static void Write(string source, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(KnobPaths.DataDir);
                var path = KnobPaths.CrashLog;
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    // Keep the newest half.
                    var text = File.ReadAllText(path);
                    File.WriteAllText(path, text[(text.Length / 2)..]);
                }
                File.AppendAllText(path, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
