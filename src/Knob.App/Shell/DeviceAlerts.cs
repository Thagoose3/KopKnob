using System.IO;
using Knob.App.Platform;

namespace Knob.App.Shell;

/// <summary>
/// Short messages for hardware events: charger plugged/unplugged, a USB drive connected/removed, and a Bluetooth
/// device connected (with its battery) or disconnected. All event-driven — nothing polls. Every event is also
/// written to events.log so a missing alert can be traced.
/// </summary>
public sealed class DeviceAlerts : IDisposable
{
    private readonly NotchWindow _window;
    private readonly BluetoothMonitor _bluetooth;
    private readonly Action<string> _show;
    private bool? _onAc;

    /// <param name="show">Shows a message: on the pill when collapsed, in the panel header when open.</param>
    public DeviceAlerts(NotchWindow window, BluetoothMonitor bluetooth, Action<string> show)
    {
        _window = window;
        _bluetooth = bluetooth;
        _show = show;
        _onAc = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus switch
        {
            System.Windows.Forms.PowerLineStatus.Online => true,
            System.Windows.Forms.PowerLineStatus.Offline => false,
            _ => null,
        };

        _window.PowerSourceChanged += OnPowerSource;
        _window.DriveChanged += OnDriveChanged;
        _bluetooth.ConnectionChanged += OnBluetooth;
        DiagLog.Write($"Alerts ready (power: {(_onAc is null ? "unknown" : _onAc.Value ? "AC" : "battery")}, Bluetooth connected: {_bluetooth.Connected.Count})");
    }

    private void OnPowerSource(bool onAc)
    {
        if (_onAc == onAc) return; // the registration sends the current state once; nothing changed
        _onAc = onAc;
        var pct = PowerStatus.BatteryPercent();
        var tail = pct is int p ? $" · {p}%" : "";
        Show(onAc ? $"⚡ Charging{tail}" : $"\U0001F50C Unplugged{tail}");
    }

    private void OnDriveChanged(char drive, bool arrived)
    {
        if (!arrived)
        {
            Show($"Drive {drive}: removed");
            return;
        }
        var label = "USB drive";
        try
        {
            var info = new DriveInfo($"{drive}:\\");
            if (info.IsReady && !string.IsNullOrWhiteSpace(info.VolumeLabel)) label = info.VolumeLabel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        Show($"\U0001F4BE {label} ({drive}:) connected");
    }

    private async void OnBluetooth(string name, bool connected)
    {
        if (!connected)
        {
            Show($"{name} disconnected");
            return;
        }
        Show($"{name} connected");

        // Windows fills in the battery level a moment after the connection.
        await Task.Delay(2500);
        var devices = await _bluetooth.ReadBatteriesAsync();
        if (devices.FirstOrDefault(d => d.Name == name)?.Battery is int pct)
            Show($"{name} · \U0001F50B {pct}%");
    }

    private void Show(string text)
    {
        DiagLog.Write("Alert: " + text);
        _show(text);
    }

    public void Dispose()
    {
        _window.PowerSourceChanged -= OnPowerSource;
        _window.DriveChanged -= OnDriveChanged;
        _bluetooth.ConnectionChanged -= OnBluetooth;
    }
}
