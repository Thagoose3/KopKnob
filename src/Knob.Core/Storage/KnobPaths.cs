namespace Knob.Core.Storage;

/// <summary>All data files live in %APPDATA%\Knob\.</summary>
public static class KnobPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Knob");

    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Notes => Path.Combine(DataDir, "notes.txt");
    public static string Todo => Path.Combine(DataDir, "todo.json");
    public static string Clipboard => Path.Combine(DataDir, "clipboard.json");
    public static string CalcHistory => Path.Combine(DataDir, "calc-history.json");
    /// <summary>PNG files of images in the clipboard history.</summary>
    public static string ClipImages => Path.Combine(DataDir, "clipimages");
    public static string Shelf => Path.Combine(DataDir, "shelf.json");
    public static string Launch => Path.Combine(DataDir, "launch.json");
    public static string CrashLog => Path.Combine(DataDir, "crash.log");
}
