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
    private readonly PillStatus _pill;
    private readonly Knob.Core.Storage.AlertSettings _settings;
    private Knob.Core.Devices.ThresholdAlarm _chargeAlarm;
    private Knob.Core.Devices.ThresholdAlarm _deviceAlarm;
    private bool? _onAc;
    private int _minutes;

    /// <param name="show">Shows a message: on the pill when collapsed, in the panel header when open.</param>
    public DeviceAlerts(NotchWindow window, BluetoothMonitor bluetooth, PillStatus pill, Knob.Core.Storage.AlertSettings settings, Action<string> show)
    {
        _window = window;
        _bluetooth = bluetooth;
        _pill = pill;
        _settings = settings;
        _show = show;
        _chargeAlarm = new(settings.ChargeLimit, settings.ChargeLimit - 5, below: false);
        _deviceAlarm = new(settings.DeviceLowBattery, settings.DeviceLowBattery + 5, below: true);
        _onAc = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus switch
        {
            System.Windows.Forms.PowerLineStatus.Online => true,
            System.Windows.Forms.PowerLineStatus.Offline => false,
            _ => null,
        };

        _window.PowerSourceChanged += OnPowerSource;
        _window.DriveChanged += OnDriveChanged;
        _bluetooth.ConnectionChanged += OnBluetooth;
        _bluetooth.Changed += OnBluetoothListChanged;
        _pill.MinuteTick += OnMinute;
        ArmChargeAlarmIfAlreadyFull();
        DiagLog.Write($"Alerts ready (power: {(_onAc is null ? "unknown" : _onAc.Value ? "AC" : "battery")}, Bluetooth connected: {_bluetooth.Connected.Count})");
    }

    private void OnPowerSource(bool onAc)
    {
        if (_onAc == onAc) return; // the registration sends the current state once; nothing changed
        _onAc = onAc;
        if (onAc) ArmChargeAlarmIfAlreadyFull();
        else _chargeAlarm.Reset("pc");
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

    /// <summary>Thresholds changed in settings: start over with the new ones.</summary>
    public void ApplySettings()
    {
        _chargeAlarm = new(_settings.ChargeLimit, _settings.ChargeLimit - 5, below: false);
        _deviceAlarm = new(_settings.DeviceLowBattery, _settings.DeviceLowBattery + 5, below: true);
        ArmChargeAlarmIfAlreadyFull();
    }

    /// <summary>Plugged in when already past the limit: nothing to warn about until it is unplugged again.</summary>
    private void ArmChargeAlarmIfAlreadyFull()
    {
        if (_settings.ChargeLimit > 0 && _onAc == true && PowerStatus.BatteryPercent() is int pct)
            _chargeAlarm.Check("pc", pct);
    }

    private void OnMinute()
    {
        // Laptop charged past the limit while charging (battery care).
        if (_settings.ChargeLimit > 0 && _onAc == true && PowerStatus.BatteryPercent() is int pct && _chargeAlarm.Check("pc", pct))
            Show($"\U0001F50B Charged to {pct}% — unplug to save battery health");

        // Bluetooth batteries change slowly and raise no event: look every 15 minutes.
        if (++_minutes % 15 == 0) _ = CheckDeviceBatteriesAsync();
    }

    private void OnBluetoothListChanged() => _ = CheckDeviceBatteriesAsync();

    private async Task CheckDeviceBatteriesAsync()
    {
        if (_settings.DeviceLowBattery <= 0 || _bluetooth.Connected.Count == 0) return;
        foreach (var d in await _bluetooth.ReadBatteriesAsync())
        {
            if (d.Battery is int level && _deviceAlarm.Check(d.Address, level))
                Show($"\U0001FAAB {d.Name} battery low · {level}%");
        }
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
        _bluetooth.Changed -= OnBluetoothListChanged;
        _pill.MinuteTick -= OnMinute;
    }
}
