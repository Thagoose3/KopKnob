using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Knob.Setup;

/// <summary>
/// Removes Knob: closes it, removes the startup entry, shortcuts and the Apps &amp; features entry, deletes the
/// files the installer listed (nothing else), optionally the user's data, and finally itself.
///   "Uninstall Knob.exe"            — asks first
///   "Uninstall Knob.exe" /S         — silent (keeps user data; /PURGE also deletes it)
///   "Uninstall Knob.exe" /FILESONLY — only delete the listed files (testing; no registry/shortcuts/self-delete)
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        var dir = Path.GetDirectoryName(Application.ExecutablePath)!;

        if (Has("/FILESONLY"))
        {
            Install.DeleteInstalledFiles(dir, Install.ReadManifest(dir).Where(f => !IsSelf(dir, f)));
            return 0;
        }

        var purge = Has("/PURGE");
        if (!Has("/S"))
        {
            Application.EnableVisualStyles();
            using var ask = new ConfirmForm();
            if (ask.ShowDialog() != DialogResult.OK) return 1;
            purge = ask.DeleteData;
        }

        try
        {
            Install.StopKnob();
            Install.RemoveRunAtStartupFor(dir);
            DeleteShortcut(Install.StartMenuShortcut, dir);
            DeleteShortcut(Install.DesktopShortcut, dir);
            Install.UnregisterUninstall();
            Install.DeleteInstalledFiles(dir, Install.ReadManifest(dir).Where(f => !IsSelf(dir, f)));
            if (purge && Directory.Exists(Install.DataDir)) Directory.Delete(Install.DataDir, recursive: true);
            DeleteSelfAfterExit(dir);
            if (!Has("/S")) MessageBox.Show("Knob was removed.", "Knob", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (!Has("/S")) MessageBox.Show("Knob couldn't be removed completely:\n\n" + ex.Message, "Knob", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    private static bool IsSelf(string dir, string rel) =>
        string.Equals(Path.GetFullPath(Path.Combine(dir, rel)), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Delete a Knob shortcut only if it points into this install.</summary>
    private static void DeleteShortcut(string lnk, string dir)
    {
        if (!File.Exists(lnk)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            string target = shell.CreateShortcut(lnk).TargetPath;
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            if (target.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) File.Delete(lnk);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
        }
    }

    /// <summary>A running exe can't delete itself: a hidden cmd waits a moment, then removes it and the empty folder.</summary>
    private static void DeleteSelfAfterExit(string dir)
    {
        var self = Application.ExecutablePath;
        var cmd = $"/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{self}\" & rmdir \"{dir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }
}

internal sealed class ConfirmForm : Form
{
    private readonly CheckBox _data;

    public ConfirmForm()
    {
        Text = "Uninstall Knob";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 170);

        var msg = new Label { Text = "Remove Knob from this PC?", Font = new Font("Segoe UI Semibold", 12f), AutoSize = true, Location = new Point(20, 18) };
        _data = new CheckBox
        {
            Text = "Also delete my notes, to-dos, clipboard history and settings",
            AutoSize = true,
            Location = new Point(22, 60),
        };
        var keep = new Label { Text = "Leave it unticked to keep them for a later reinstall.", ForeColor = SystemColors.GrayText, AutoSize = true, Location = new Point(40, 84) };
        var remove = new Button { Text = "Uninstall", DialogResult = DialogResult.OK, Location = new Point(230, 124), Size = new Size(90, 30) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(328, 124), Size = new Size(80, 30) };
        AcceptButton = remove;
        CancelButton = cancel;
        Controls.AddRange([msg, _data, keep, remove, cancel]);
    }

    public bool DeleteData => _data.Checked;
}
