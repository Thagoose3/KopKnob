using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Knob.Setup;

/// <summary>
/// Where Knob is installed and how it is wired into Windows (shared by the installer and the uninstaller).
/// Per-user install: %LOCALAPPDATA%\Programs\Knob, HKCU keys only — no admin rights needed.
/// </summary>
internal static class Install
{
    public const string AppName = "Knob";
    public const string ExeName = "Knob.exe";
    public const string UninstallExeName = "Uninstall Knob.exe";
    /// <summary>Relative paths of every file the installer wrote, so uninstall removes exactly those.</summary>
    public const string ManifestName = "install-files.txt";

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Knob";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Knob"; // same value Knob's own "Run at startup" uses

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    /// <summary>The folder of an existing install, from the Apps &amp; features entry.</summary>
    public static string? InstalledDir()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("InstallLocation") as string;
    }

    public static string? InstalledVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("DisplayVersion") as string;
    }

    /// <summary>Close every running Knob (only one can run at a time, and its files must be replaceable).</summary>
    public static int StopKnob()
    {
        var stopped = 0;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try
            {
                p.Kill();
                p.WaitForExit(5000);
                stopped++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                p.Dispose();
            }
        }
        return stopped;
    }

    public static bool IsKnobRunning() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)).Any();

    public static void CreateShortcut(string lnkPath, string target, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell not available");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(lnkPath);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = description;
            link.IconLocation = target + ",0";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    public static void SetRunAtStartup(string? exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (exePath is null) key.DeleteValue(RunValue, throwOnMissingValue: false);
        else key.SetValue(RunValue, $"\"{exePath}\"");
    }

    /// <summary>Remove the startup entry only if it points into <paramref name="dir"/> (not some other copy of Knob).</summary>
    public static void RemoveRunAtStartupFor(string dir)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(RunValue) is string cmd && cmd.Trim('"').StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    public static void RegisterUninstall(string dir, string version, long sizeBytes)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        var exe = Path.Combine(dir, ExeName);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", AppName);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", dir);
        key.SetValue("UninstallString", $"\"{Path.Combine(dir, UninstallExeName)}\"");
        key.SetValue("EstimatedSize", (int)(sizeBytes / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void UnregisterUninstall() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

    public static IReadOnlyList<string> ReadManifest(string dir)
    {
        var file = Path.Combine(dir, ManifestName);
        return File.Exists(file) ? File.ReadAllLines(file).Where(l => l.Length > 0).ToList() : [];
    }

    /// <summary>Delete the listed files, then any folders left empty (deepest first). Never touches other files.</summary>
    public static void DeleteInstalledFiles(string dir, IEnumerable<string> relativePaths)
    {
        var root = Path.GetFullPath(dir).TrimEnd('\\');
        foreach (var rel in relativePaths)
        {
            var path = Path.GetFullPath(Path.Combine(root, rel));
            if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) continue; // stay inside the install folder
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        if (!Directory.Exists(root)) return;
        foreach (var sub in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
