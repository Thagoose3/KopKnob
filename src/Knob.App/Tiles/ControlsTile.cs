using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Knob.App.Platform;
using Knob.Core.Devices;
using Knob.Core.Layout;

namespace Knob.App.Tiles;

/// <summary>
/// Controls page: master volume (also the mouse wheel on the knob), laptop screen brightness, and the connected
/// Bluetooth devices with their battery. Everything is read when the page opens; nothing polls.
/// </summary>
public sealed class ControlsTile : TileBase
{
    private static readonly Brush LowBattery = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));

    private readonly BluetoothMonitor _bluetooth;
    private readonly Dispatcher _dispatcher;
    private float? _volume;
    private bool _muted;
    private int? _brightness;
    private IReadOnlyList<ConnectedDevice> _devices = [];
    private bool _visible;

    private int? _pendingBrightness;
    private bool _brightnessWorker;

    public ControlsTile(BluetoothMonitor bluetooth, Dispatcher dispatcher)
    {
        _bluetooth = bluetooth;
        _dispatcher = dispatcher;
        _bluetooth.Changed += () => { if (_visible) _ = RefreshDevicesAsync(); };
    }

    private event Action? Refreshed;

    public override string Id => "controls";
    public override string Title => "Controls";
    public override IReadOnlyList<TileSize> SupportedSizes { get; } = [TileSize.L];

    /// <summary>Step the volume by a mouse-wheel delta; returns the new percent (null: no audio device).</summary>
    public int? Nudge(int wheelDelta)
    {
        if (AudioVolume.Get() is not float now) return null;
        var next = VolumeStep.Apply(now, wheelDelta);
        AudioVolume.Set(next);
        _volume = next;
        _muted = next <= 0 && (AudioVolume.IsMuted() ?? false);
        Refreshed?.Invoke();
        return VolumeStep.Percent(next);
    }

    public override void OnPanelOpened()
    {
        _visible = true;
        _volume = AudioVolume.Get();
        _muted = AudioVolume.IsMuted() ?? false;
        Refreshed?.Invoke();
        Task.Run(() =>
        {
            var b = ScreenBrightness.Get();
            _dispatcher.BeginInvoke(() => { _brightness = b; Refreshed?.Invoke(); });
        });
        _ = RefreshDevicesAsync();
    }

    public override void OnPanelClosed() => _visible = false;

    private async Task RefreshDevicesAsync()
    {
        _devices = await _bluetooth.ReadBatteriesAsync();
        Refreshed?.Invoke();
    }

    /// <summary>Apply the latest requested brightness on a worker; slider moves in between are coalesced.</summary>
    private void RequestBrightness(int percent)
    {
        _brightness = percent;
        _pendingBrightness = percent;
        if (_brightnessWorker) return;
        _brightnessWorker = true;
        Task.Run(() =>
        {
            while (true)
            {
                var p = _dispatcher.Invoke(() =>
                {
                    var v = _pendingBrightness;
                    _pendingBrightness = null;
                    if (v is null) _brightnessWorker = false;
                    return v;
                });
                if (p is not int value) return;
                ScreenBrightness.Set(value);
            }
        });
    }

    public override FrameworkElement CreateView(TileSize size)
    {
        // Volume
        var volPercent = Ui.Text("", 12, Ui.Subtle, FontWeights.SemiBold);
        var (volSlider, setVol) = Pages.Slider(v =>
        {
            _volume = (float)v;
            _muted = false;
            AudioVolume.Set((float)v);
            Refreshed?.Invoke();
        });
        var muteIcon = new TextBlock { FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 15, Foreground = Ui.Fg };
        var mute = new Button { Content = muteIcon, Width = 34, Height = 30, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Mute" };
        mute.Click += (_, _) =>
        {
            _muted = !_muted;
            AudioVolume.SetMuted(_muted);
            Refreshed?.Invoke();
        };
        var volumeCard = SliderCard("Volume", volPercent, mute, volSlider, "Tip: scroll the mouse wheel on the knob");

        // Brightness
        var brightPercent = Ui.Text("", 12, Ui.Subtle, FontWeights.SemiBold);
        var (brightSlider, setBright) = Pages.Slider(v => { RequestBrightness((int)Math.Round(v * 100)); Refreshed?.Invoke(); });
        var sun = new Border
        {
            Width = 34, Height = 30, Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(8), Background = Ui.Control,
            Child = new TextBlock { Text = Ui.IconBrightness, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 15, Foreground = Ui.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var brightnessCard = SliderCard("Brightness", brightPercent, sun, brightSlider, null);
        var noBrightness = Ui.Text("Not available on this display", 11, Ui.Dim);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(volumeCard);
        left.Children.Add(brightnessCard);

        // Devices
        var deviceList = new StackPanel();
        var devicesTitle = Ui.Text("Connected devices", 12, Ui.Subtle, FontWeights.SemiBold);
        devicesTitle.Margin = new Thickness(0, 0, 0, 8);
        var devicesPanel = new StackPanel();
        devicesPanel.Children.Add(devicesTitle);
        devicesPanel.Children.Add(deviceList);
        var devicesCard = Pages.Card(new ScrollViewer { Content = devicesPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });
        devicesCard.Width = 290;
        devicesCard.Margin = new Thickness(10, 4, 4, 4);

        var root = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
        DockPanel.SetDock(devicesCard, Dock.Right);
        root.Children.Add(devicesCard);
        root.Children.Add(left);
        Pages.Smooth(root);

        void Refresh()
        {
            if (_volume is float v)
            {
                setVol(_muted ? 0 : v);
                volPercent.Text = _muted ? "Muted" : $"{VolumeStep.Percent(v)}%";
                muteIcon.Text = _muted ? Ui.IconMute : Ui.IconVolume;
                volSlider.IsEnabled = mute.IsEnabled = true;
            }
            else
            {
                volPercent.Text = "No audio device";
                muteIcon.Text = Ui.IconMute;
                volSlider.IsEnabled = mute.IsEnabled = false;
            }

            if (_brightness is int b)
            {
                setBright(b / 100.0);
                brightPercent.Text = $"{b}%";
                brightSlider.Visibility = Visibility.Visible;
                if (brightSlider.Parent is DockPanel dp && dp.Children.Contains(noBrightness)) dp.Children.Remove(noBrightness);
            }
            else
            {
                brightPercent.Text = "";
                brightSlider.Visibility = Visibility.Collapsed;
                if (brightSlider.Parent is DockPanel dp && !dp.Children.Contains(noBrightness)) dp.Children.Add(noBrightness);
            }

            deviceList.Children.Clear();
            if (_devices.Count == 0)
                deviceList.Children.Add(Ui.Text("No Bluetooth devices connected", 11.5, Ui.Dim));
            foreach (var d in _devices) deviceList.Children.Add(DeviceRow(d));
        }

        Ui.WhileLoaded(root, () => { Refreshed += Refresh; Refresh(); }, () => Refreshed -= Refresh);
        Refresh();
        return root;
    }

    private static Border SliderCard(string title, TextBlock percent, FrameworkElement leading, FrameworkElement slider, string? hint)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(percent, Dock.Right);
        head.Children.Add(percent);
        head.Children.Add(Ui.Text(title, 13, weight: FontWeights.SemiBold));

        var row = new DockPanel();
        DockPanel.SetDock(leading, Dock.Left);
        row.Children.Add(leading);
        row.Children.Add(slider);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(row);
        if (hint is not null)
        {
            var h = Ui.Text(hint, 10, Ui.Dim);
            h.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(h);
        }
        return Pages.Card(stack);
    }

    private static FrameworkElement DeviceRow(ConnectedDevice d)
    {
        var icon = new TextBlock
        {
            Text = Ui.IconBluetooth,
            FontFamily = Ui.Res<FontFamily>("IconFont"),
            FontSize = 13,
            Foreground = Ui.Subtle,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = Ui.Text(d.Name, 12);
        name.TextTrimming = TextTrimming.CharacterEllipsis;

        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        if (d.Battery is int level)
        {
            var low = level <= 20;
            var pct = Ui.Text($"{level}%", 11.5, low ? LowBattery : Ui.Subtle, FontWeights.SemiBold);
            pct.Width = 36;
            pct.TextAlignment = TextAlignment.Right;
            var (bar, set) = Pages.Bar(low ? LowBattery : Ui.Accent, 5);
            bar.Width = 54;
            bar.Margin = new Thickness(8, 0, 0, 0);
            set(level / 100.0);
            DockPanel.SetDock(pct, Dock.Right);
            DockPanel.SetDock(bar, Dock.Right);
            row.Children.Add(pct);
            row.Children.Add(bar);
        }
        else
        {
            var none = Ui.Text("—", 11.5, Ui.Dim);
            DockPanel.SetDock(none, Dock.Right);
            row.Children.Add(none);
        }
        row.Children.Add(name);
        return row;
    }
}
