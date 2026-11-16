using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Knob.Setup;

/// <summary>Options picked in the installer window (or on the command line).</summary>
internal sealed class InstallOptions
{
    public string Dir { get; set; } = Install.DefaultDir;
    public bool RunAtStartup { get; set; } = true;
    public bool DesktopShortcut { get; set; }
    public bool LaunchWhenDone { get; set; } = true;
    /// <summary>Only unpack the files (no registry, shortcuts or closing Knob). For testing the package.</summary>
    public bool FilesOnly { get; set; }
}

/// <summary>The install steps. Reports progress as 0–1 with a short status text.</summary>
internal static class Installer
{
    public static Stream? OpenPayload() => typeof(Installer).Assembly.GetManifestResourceStream("payload.zip");

    public static string Version =>
        typeof(Installer).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    /// <summary>Install into <see cref="InstallOptions.Dir"/>. Throws on failure (the caller shows the message).</summary>
    public static void Run(InstallOptions o, Action<double, string> progress)
    {
        using var payload = OpenPayload() ?? throw new InvalidOperationException("This installer has no app inside (build it with scripts\\build-installer.ps1).");
        var dir = Path.GetFullPath(o.Dir).TrimEnd('\\');
        var root = dir + "\\";

        if (!o.FilesOnly)
        {
            progress(0.02, "Closing Knob…");
            Install.StopKnob();
        }

        // Files from an older version that this one no longer has are removed; nothing else in the folder is touched.
        var old = Install.ReadManifest(dir);

        progress(0.05, "Copying files…");
        Directory.CreateDirectory(dir);
        var written = new List<string>();
        long size = 0;
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
            var total = files.Sum(e => e.Length);
            long done = 0;
            foreach (var entry in files)
            {
                var rel = entry.FullName.Replace('/', '\\');
                var target = Path.GetFullPath(Path.Combine(dir, rel));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ExtractWithRetry(entry, target);
                written.Add(rel);
                done += entry.Length;
                size += entry.Length;
                progress(0.05 + 0.85 * done / Math.Max(1, total), "Copying " + entry.Name);
            }
        }
        written.Add(Install.ManifestName);
        File.WriteAllLines(Path.Combine(dir, Install.ManifestName), written);
        Install.DeleteInstalledFiles(dir, old.Except(written, StringComparer.OrdinalIgnoreCase));

        if (o.FilesOnly)
        {
            progress(1, "Files unpacked.");
            return;
        }

        progress(0.93, "Adding shortcuts…");
        var exe = Path.Combine(dir, Install.ExeName);
        Install.CreateShortcut(Install.StartMenuShortcut, exe, "Knob — a notch for Windows");
        if (o.DesktopShortcut) Install.CreateShortcut(Install.DesktopShortcut, exe, "Knob — a notch for Windows");
        else if (File.Exists(Install.DesktopShortcut)) File.Delete(Install.DesktopShortcut);

        Install.SetRunAtStartup(o.RunAtStartup ? exe : null);
        Install.RegisterUninstall(dir, Version, size);

        if (o.LaunchWhenDone)
        {
            progress(0.98, "Starting Knob…");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
        }
        progress(1, "Knob is installed.");
    }

    /// <summary>A just-closed Knob can hold its exe for a moment: retry briefly.</summary>
    private static void ExtractWithRetry(ZipArchiveEntry entry, string target)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                entry.ExtractToFile(target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                System.Threading.Thread.Sleep(300);
            }
        }
    }
}
