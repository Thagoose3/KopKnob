using System.IO;
using Knob.App.Platform;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Knob.App.Shell;

/// <summary>
/// Short pill messages for hardware events: charger plugged/unplugged, a USB drive connected/removed, and a
/// Bluetooth device connected (with its battery) or disconnected. All event-driven — nothing polls.
/// </summary>
public sealed class DeviceAlerts : IDisposable
{
    private static readonly TimeSpan Show = TimeSpan.FromSeconds(3);

    private readonly NotchWindow _window;
    private readonly BluetoothMonitor _bluetooth;
    private Forms.PowerLineStatus _line;

    public DeviceAlerts(NotchWindow window, BluetoothMonitor bluetooth)
    {
        _window = window;
        _bluetooth = bluetooth;
        _line = Forms.SystemInformation.PowerStatus.PowerLineStatus;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _window.DriveChanged += OnDriveChanged;
        _bluetooth.ConnectionChanged += OnBluetooth;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange) return;
        _window.Dispatcher.BeginInvoke(() =>
        {
            var line = Forms.SystemInformation.PowerStatus.PowerLineStatus;
            if (line == _line || line == Forms.PowerLineStatus.Unknown) return;
            _line = line;
            if (PowerStatus.BatteryPercent() is not int pct) return; // desktop PC: nothing to say
            _window.ShowPillMessage(line == Forms.PowerLineStatus.Online
                ? $"⚡ Charging · {pct}%"
                : $"\U0001F50C Unplugged · {pct}%", Show);
        });
    }

    private void OnDriveChanged(char drive, bool arrived)
    {
        if (!arrived)
        {
            _window.ShowPillMessage($"Drive {drive}: removed", Show);
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
        _window.ShowPillMessage($"\U0001F4BE {label} ({drive}:) connected", Show);
    }

    private async void OnBluetooth(string name, bool connected)
    {
        if (!connected)
        {
            _window.ShowPillMessage($"{name} disconnected", Show);
            return;
        }
        _window.ShowPillMessage($"{name} connected", Show);

        // Windows fills in the battery level a moment after the connection.
        await Task.Delay(2500);
        var devices = await _bluetooth.ReadBatteriesAsync();
        if (devices.FirstOrDefault(d => d.Name == name)?.Battery is int pct)
            _window.ShowPillMessage($"{name} · \U0001F50B {pct}%", Show);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _window.DriveChanged -= OnDriveChanged;
        _bluetooth.ConnectionChanged -= OnBluetooth;
    }
}
