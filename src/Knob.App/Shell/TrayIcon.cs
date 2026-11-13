using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Knob.App.Platform;

namespace Knob.App.Shell;

/// <summary>Tray icon with Open / Edit layout / Position / Run at startup / Quit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _runAtStartup;

    public TrayIcon(bool runAtStartup)
    {
        _icon = CreateIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke());

        menu.Items.Add("Edit layout", null, (_, _) => EditLayoutRequested?.Invoke());

        // Quick placements; the knob can also be dragged out of the open panel and dropped on an edge.
        var positionMenu = new ToolStripMenuItem("Position");
        foreach (var (label, preset) in new[]
                 {
                     ("Top center", DockPreset.TopCenter),
                     ("Top left", DockPreset.TopLeft),
                     ("Top right", DockPreset.TopRight),
                     ("Left side", DockPreset.LeftSide),
                     ("Right side", DockPreset.RightSide),
                 })
        {
            positionMenu.DropDownItems.Add(label, null, (_, _) => PresetChosen?.Invoke(preset));
        }
        menu.Items.Add(positionMenu);

        _runAtStartup = new ToolStripMenuItem("Run at startup") { Checked = runAtStartup, CheckOnClick = true };
        _runAtStartup.CheckedChanged += (_, _) => RunAtStartupToggled?.Invoke(_runAtStartup.Checked);
        menu.Items.Add(_runAtStartup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Knob",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public event Action? OpenRequested;
    public event Action? QuitRequested;
    public event Action<bool>? RunAtStartupToggled;
    public event Action<DockPreset>? PresetChosen;
    public event Action? EditLayoutRequested;

    /// <summary>Setting this raises <see cref="RunAtStartupToggled"/> too, so handlers must be idempotent.</summary>
    public bool RunAtStartupChecked
    {
        get => _runAtStartup.Checked;
        set => _runAtStartup.Checked = value;
    }

    public void ShowWarning(string title, string text) =>
        _notifyIcon.ShowBalloonTip(6000, title, text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    /// <summary>A small accent-colored pill; no .ico asset needed.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var path = new GraphicsPath();
            const int x = 2, y = 9, w = 28, h = 14, r = 14;
            path.AddArc(x, y, r, h, 90, 180);
            path.AddArc(x + w - r, y, r, h, 270, 180);
            path.CloseFigure();
            using var brush = new SolidBrush(Color.FromArgb(0x7C, 0x5C, 0xFF));
            g.FillPath(brush, path);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
