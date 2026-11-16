using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Knob.Setup;

/// <summary>One-page installer: where to install, three options, Install. Dark like Knob.</summary>
internal sealed class SetupForm : Form
{
    private static readonly Color Bg = Color.FromArgb(0x16, 0x16, 0x1C);
    private static readonly Color Card = Color.FromArgb(0x22, 0x22, 0x2A);
    private static readonly Color Fg = Color.FromArgb(0xF2, 0xF2, 0xF5);
    private static readonly Color Subtle = Color.FromArgb(0x9A, 0x9A, 0xA5);
    private static readonly Color Accent = Color.FromArgb(0x7C, 0x5C, 0xFF);

    private readonly InstallOptions _options = new();
    private readonly TextBox _dir;
    private readonly CheckBox _startup;
    private readonly CheckBox _desktop;
    private readonly CheckBox _launch;
    private readonly Button _install;
    private readonly Button _cancel;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private bool _done;

    public SetupForm()
    {
        var existing = Install.InstalledDir();
        if (existing is not null) _options.Dir = existing;
        var update = existing is not null;

        Text = "Install Knob";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 330);
        Padding = new Padding(24);

        var title = new Label { Text = update ? $"Update Knob to {Installer.Version}" : $"Install Knob {Installer.Version}", Font = new Font("Segoe UI Semibold", 16f), AutoSize = true, Location = new Point(24, 20) };
        var sub = new Label
        {
            Text = update ? $"Version {Install.InstalledVersion() ?? "?"} is installed. Your notes and settings are kept."
                          : "A Dynamic Island-style notch for Windows. No admin rights needed.",
            ForeColor = Subtle, AutoSize = true, Location = new Point(26, 58),
        };

        var dirLabel = new Label { Text = "Install to", ForeColor = Subtle, AutoSize = true, Location = new Point(26, 96) };
        _dir = new TextBox { Text = _options.Dir, Location = new Point(26, 118), Width = 360, BackColor = Card, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true };
        var browse = FlatButton("Change…", Card);
        browse.Location = new Point(394, 116);
        browse.Size = new Size(80, 27);
        browse.Enabled = !update; // updates go where Knob already is
        browse.Click += (_, _) => PickFolder();

        _startup = Check("Start Knob when I sign in to Windows", true, 160);
        _desktop = Check("Add a desktop shortcut", false, 188);
        _launch = Check("Open Knob when setup finishes", true, 216);

        _progress = new ProgressBar { Location = new Point(26, 256), Width = 448, Height = 6, Visible = false, Style = ProgressBarStyle.Continuous, Maximum = 1000 };
        _status = new Label { ForeColor = Subtle, AutoSize = true, Location = new Point(26, 268) };

        _install = FlatButton(update ? "Update" : "Install", Accent);
        _install.ForeColor = Color.White;
        _install.Location = new Point(290, 284);
        _install.Size = new Size(100, 32);
        _install.Click += async (_, _) => await InstallAsync();
        _cancel = FlatButton("Cancel", Card);
        _cancel.Location = new Point(398, 284);
        _cancel.Size = new Size(76, 32);
        _cancel.Click += (_, _) => Close();
        AcceptButton = _install;
        CancelButton = _cancel;

        if (Install.IsKnobRunning())
            _status.Text = "Knob is running — setup will close it first.";

        Controls.AddRange([title, sub, dirLabel, _dir, browse, _startup, _desktop, _launch, _progress, _status, _install, _cancel]);
        _dir.TabStop = false;
        ActiveControl = _install; // not the read-only path, which would show up highlighted
    }

    private CheckBox Check(string text, bool on, int y) => new()
    {
        Text = text,
        Checked = on,
        AutoSize = true,
        Location = new Point(26, y),
        ForeColor = Fg,
        FlatStyle = FlatStyle.Flat,
    };

    private static Button FlatButton(string text, Color back)
    {
        var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Fg, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private void PickFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Knob will be installed in a \"Knob\" folder inside the folder you pick.", SelectedPath = Path.GetDirectoryName(_dir.Text) };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var picked = dlg.SelectedPath;
        _dir.Text = string.Equals(Path.GetFileName(picked.TrimEnd('\\')), Install.AppName, StringComparison.OrdinalIgnoreCase)
            ? picked
            : Path.Combine(picked, Install.AppName); // always our own folder, so uninstall never touches anything else
    }

    private async Task InstallAsync()
    {
        if (_done)
        {
            Close();
            return;
        }

        _options.Dir = _dir.Text;
        _options.RunAtStartup = _startup.Checked;
        _options.DesktopShortcut = _desktop.Checked;
        _options.LaunchWhenDone = _launch.Checked;

        foreach (Control c in Controls) if (c is CheckBox or Button) c.Enabled = false;
        _progress.Visible = true;
        try
        {
            await Task.Run(() => Installer.Run(_options, (f, s) => BeginInvoke(new Action(() =>
            {
                _progress.Value = (int)(Math.Min(1, f) * 1000);
                _status.Text = s;
            }))));
            _done = true;
            _status.Text = _options.LaunchWhenDone ? "Done! Knob is at the top of your screen." : "Done! Find Knob in the Start menu.";
            _install.Text = "Close";
            _install.Enabled = true;
            _cancel.Visible = false;
        }
        catch (Exception ex)
        {
            _progress.Visible = false;
            _status.Text = "";
            MessageBox.Show(this, "Setup couldn't finish:\n\n" + ex.Message, "Knob setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            foreach (Control c in Controls) if (c is CheckBox or Button) c.Enabled = true;
        }
    }
}
