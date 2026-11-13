using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.App.Shell;
using Knob.App.Tiles;
using Microsoft.Win32;

namespace Knob.App.Tools;

/// <summary>Full-panel QR maker: text → styled QR; Copy puts a 1024×1024 PNG on the clipboard, Save… writes a PNG.</summary>
public sealed class QrTool(NotchController controller)
{
    private const int ExportPixels = 1024;
    private const int MaxPrefill = 500;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x7C, 0x5C, 0xFF),
        Color.FromRgb(0x14, 0xB8, 0xA6),
        Color.FromRgb(0xEC, 0x48, 0x99),
        Color.FromRgb(0xEF, 0x44, 0x44),
    ];

    private QrStyle _style = QrStyle.Square;
    private Color _color = Palette[0];

    public FrameworkElement CreateView()
    {
        var input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 64,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        var prefill = ClipboardUtil.TryGetText();
        if (prefill is not null && prefill.Length <= MaxPrefill) input.Text = prefill;

        var preview = new Image { Width = 180, Height = 180, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        var previewBox = new Border { Width = 186, Height = 186, Background = Brushes.White, CornerRadius = new CornerRadius(8), Child = preview, Margin = new Thickness(12, 0, 0, 0) };
        var status = Ui.Text("", 11, Ui.Subtle);
        var copy = new Button { Content = "Copy", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 6, 0) };
        var save = new Button { Content = "Save…", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 10, 0) };
        var swatches = new List<(Ellipse Ring, Color Color)>();
        bool[,]? matrix = null;

        // Style buttons
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var styleButtons = new List<ToggleButton>();
        foreach (var s in Enum.GetValues<QrStyle>())
        {
            var b = new ToggleButton { Content = Ui.Text(s.ToString(), 11), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0), Tag = s };
            b.Click += (_, _) => { _style = (QrStyle)b.Tag; Update(); };
            styleButtons.Add(b);
            styleRow.Children.Add(b);
        }

        // Colour swatches
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var c in Palette)
        {
            var ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = 2 };
            var dot = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(c) };
            var g = new Grid { Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            g.Children.Add(ring);
            g.Children.Add(dot);
            var color = c;
            g.MouseLeftButtonUp += (_, _) => { _color = color; Update(); };
            swatches.Add((ring, c));
            colorRow.Children.Add(g);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(copy);
        actions.Children.Add(save);
        actions.Children.Add(status);

        var left = new StackPanel();
        left.Children.Add(Ui.WithHint(input, "Text or link to encode"));
        left.Children.Add(styleRow);
        left.Children.Add(colorRow);
        left.Children.Add(actions);

        var root = new DockPanel();
        DockPanel.SetDock(previewBox, Dock.Right);
        root.Children.Add(previewBox);
        root.Children.Add(left);

        void Update()
        {
            foreach (var b in styleButtons) b.IsChecked = (QrStyle)b.Tag == _style;
            foreach (var (ring, c) in swatches) ring.Stroke = c == _color ? Ui.Fg : Brushes.Transparent;

            matrix = QrRenderer.BuildMatrix(input.Text, out var error);
            copy.IsEnabled = save.IsEnabled = matrix is not null;
            if (matrix is null)
            {
                preview.Source = null;
                status.Text = error ?? "";
                return;
            }
            status.Text = "";
            // Integer pixels per module for a crisp preview.
            var n = matrix.GetLength(0);
            preview.Source = QrRenderer.Render(matrix, n * Math.Max(1, 360 / n), _style, _color);
        }

        input.TextChanged += (_, _) => Update();

        copy.Click += async (_, _) =>
        {
            if (matrix is null) return;
            var bmp = QrRenderer.Render(matrix, ExportPixels, _style, _color);
            var data = new DataObject();
            data.SetImage(bmp);
            var png = new MemoryStream(EncodePng(bmp));
            data.SetData("PNG", png);
            status.Text = await ClipboardUtil.TrySetAsync(data) ? "Copied" : "Clipboard busy";
        };

        save.Click += (_, _) =>
        {
            if (matrix is null) return;
            var dlg = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "qr.png", AddExtension = true };
            controller.KeepOpen = true; // the dialog takes focus; don't collapse underneath it
            try
            {
                if (dlg.ShowDialog(Window.GetWindow(root)) != true) return;
                File.WriteAllBytes(dlg.FileName, EncodePng(QrRenderer.Render(matrix, ExportPixels, _style, _color)));
                status.Text = "Saved";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.Text = "Couldn't save";
            }
            finally
            {
                controller.KeepOpen = false;
            }
        };

        root.Loaded += (_, _) =>
        {
            Update();
            root.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { input.Focus(); input.SelectAll(); });
        };
        return root;
    }

    private static byte[] EncodePng(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
