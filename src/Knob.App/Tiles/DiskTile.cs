using System.IO;
using System.Windows;
using System.Windows.Controls;
using Knob.Core.Layout;
using Knob.Core.Storage;

namespace Knob.App.Tiles;

/// <summary>Free space on the system drive. Read only when the panel opens.</summary>
public sealed class DiskTile : TileBase
{
    /// <summary>Raised after the drive was (re)read.</summary>
    public event Action? Changed;
    private string _name = "C:";
    private long _free, _total;

    public string DriveName => _name;
    public long FreeBytes => _free;
    public long TotalBytes => _total;

    /// <summary>Open the drive in File Explorer.</summary>
    public void OpenInExplorer()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", _name + "\\") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // nothing sensible to do
        }
    }

    public override string Id => "disk";
    public override string Title => "Disk";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.S, TileSize.W];

    public override void OnPanelOpened()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            _name = root.TrimEnd('\\');
            _free = drive.AvailableFreeSpace;
            _total = drive.TotalSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _free = _total = 0;
        }
        Changed?.Invoke();
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        var title = Ui.Title("");
        var free = Ui.Text("", 13, weight: FontWeights.SemiBold);
        var total = Ui.Text("", 9, Ui.Subtle);

        var bar = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 4) };
        var used = new ColumnDefinition();
        var rest = new ColumnDefinition();
        bar.ColumnDefinitions.Add(used);
        bar.ColumnDefinitions.Add(rest);
        var track = new Border { Background = Ui.Control, CornerRadius = new CornerRadius(2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = Ui.Accent, CornerRadius = new CornerRadius(2) });

        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(title);
        root.Children.Add(free);
        root.Children.Add(bar);
        root.Children.Add(total);

        void Refresh()
        {
            title.Text = _name;
            if (_total <= 0)
            {
                free.Text = "—";
                total.Text = "";
                return;
            }
            free.Text = ByteFormatter.Format(_free);
            total.Text = $"free of {ByteFormatter.Format(_total)}";
            var usedBytes = _total - _free;
            used.Width = new GridLength(Math.Max(1, usedBytes), GridUnitType.Star);
            rest.Width = new GridLength(Math.Max(1, _free), GridUnitType.Star);
        }

        Ui.WhileLoaded(root, () => { Changed += Refresh; Refresh(); }, () => Changed -= Refresh);
        return root;
    }
}
