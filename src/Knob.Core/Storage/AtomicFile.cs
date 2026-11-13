using System.Text;

namespace Knob.Core.Storage;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Write UTF-8 to <c>path.tmp</c>, then move it over <paramref name="path"/>. Creates the directory.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, contents, Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>Read the file, or <paramref name="fallback"/> if it is missing or unreadable.</summary>
    public static string ReadAllTextOrDefault(string path, string fallback = "")
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
