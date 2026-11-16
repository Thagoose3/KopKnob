using System.Windows.Threading;
using Knob.Core.Devices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Knob.App.Platform;

public sealed record ConnectedDevice(string Name, string Address, int? Battery);

/// <summary>
/// Paired Bluetooth devices (classic + LE): which are connected, raised as Connected/Disconnected events
/// (event-driven DeviceWatchers, nothing polls), and their battery level, which Windows keeps on the device node
/// (the same value Settings shows).
/// </summary>
public sealed class BluetoothMonitor : IDisposable
{
    private const string IsConnected = "System.Devices.Aep.IsConnected";
    private const string DeviceAddress = "System.Devices.Aep.DeviceAddress";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, (string Name, string Address, bool Connected)> _endpoints = [];
    private readonly List<DeviceWatcher> _watchers = [];
    private int _pendingEnumerations;

    public BluetoothMonitor(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Raised on the UI thread after start-up enumeration: a device connected (true) or disconnected (false).</summary>
    public event Action<string, bool>? ConnectionChanged;
    /// <summary>Raised on the UI thread whenever the set of connected devices may have changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        string[] props = [IsConnected, DeviceAddress];
        foreach (var selector in new[]
                 {
                     BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                     BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                 })
        {
            try
            {
                var w = DeviceInformation.CreateWatcher(selector, props, DeviceInformationKind.AssociationEndpoint);
                w.Added += (_, info) => Post(() => Upsert(info.Id, info.Name, info.Properties, initial: _pendingEnumerations > 0));
                w.Updated += (_, u) => Post(() => Update(u));
                w.Removed += (_, u) => Post(() => { _endpoints.Remove(u.Id); Changed?.Invoke(); });
                w.EnumerationCompleted += (_, _) => Post(() => _pendingEnumerations--);
                _pendingEnumerations++;
                _watchers.Add(w);
                w.Start();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                // no Bluetooth radio / service disabled
            }
        }
    }

    public IReadOnlyList<(string Name, string Address)> Connected =>
        _endpoints.Values.Where(e => e.Connected).Select(e => (e.Name, e.Address)).OrderBy(e => e.Name).ToList();

    /// <summary>Connected devices with their battery level (null when the device doesn't report one).</summary>
    public async Task<IReadOnlyList<ConnectedDevice>> ReadBatteriesAsync()
    {
        var connected = Connected;
        if (connected.Count == 0) return [];

        var levels = new Dictionary<string, int>();
        try
        {
            var nodes = await DeviceInformation.FindAllAsync("System.Devices.DeviceInstanceId:~<\"BTH\"", [BatteryKey], DeviceInformationKind.Device);
            foreach (var n in nodes)
            {
                if (!n.Properties.TryGetValue(BatteryKey, out var b) || b is null) continue;
                if (BluetoothAddress.FromId(n.Id) is string addr) levels[addr] = Convert.ToInt32(b);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or FormatException or InvalidCastException)
        {
        }

        return connected.Select(c => new ConnectedDevice(c.Name, c.Address, levels.TryGetValue(c.Address, out var l) ? l : null)).ToList();
    }

    private void Upsert(string id, string name, IReadOnlyDictionary<string, object> props, bool initial)
    {
        var connected = props.TryGetValue(IsConnected, out var c) && c is true;
        var address = props.TryGetValue(DeviceAddress, out var a) && a is string s
            ? s.Replace(":", "").ToUpperInvariant()
            : BluetoothAddress.FromId(id) ?? id;
        _endpoints[id] = (string.IsNullOrWhiteSpace(name) ? "Bluetooth device" : name, address, connected);
        if (connected && !initial) ConnectionChanged?.Invoke(_endpoints[id].Name, true);
        Changed?.Invoke();
    }

    private void Update(DeviceInformationUpdate u)
    {
        if (!_endpoints.TryGetValue(u.Id, out var e)) return;
        if (!u.Properties.TryGetValue(IsConnected, out var c) || c is not bool now || now == e.Connected) return;
        _endpoints[u.Id] = e with { Connected = now };
        DiagLog.Write($"Bluetooth {e.Name}: {(now ? "connected" : "disconnected")}");
        if (_pendingEnumerations <= 0) ConnectionChanged?.Invoke(e.Name, now);
        Changed?.Invoke();
    }

    private void Post(Action a) => _dispatcher.BeginInvoke(a);

    public void Dispose()
    {
        foreach (var w in _watchers)
        {
            try
            {
                if (w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) w.Stop();
            }
            catch (InvalidOperationException)
            {
            }
        }
        _watchers.Clear();
    }
}
