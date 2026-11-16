using System.Runtime.InteropServices;
using System.Windows.Threading;
using Knob.Core.Devices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Knob.App.Platform;

/// <summary>
/// Which apps are using the camera / microphone right now, from the same registry data Windows' own privacy
/// indicator uses (CapabilityAccessManager\ConsentStore: an app is "in use" while LastUsedTimeStop is 0).
/// A background thread sleeps on RegNotifyChangeKeyValue and rescans only when Windows writes there.
/// </summary>
public sealed class PrivacyWatcher : IDisposable
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private const int REG_NOTIFY_CHANGE_NAME = 0x1, REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    private readonly Dispatcher _dispatcher;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;

    public PrivacyWatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public IReadOnlyList<string> CameraApps { get; private set; } = [];
    public IReadOnlyList<string> MicrophoneApps { get; private set; } = [];

    /// <summary>Raised on the UI thread when the camera or microphone users changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        Rescan(raise: false);
        _thread = new Thread(Watch) { IsBackground = true, Name = "Knob privacy watcher", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Watch()
    {
        using var cam = Registry.CurrentUser.OpenSubKey(Root + "webcam");
        using var mic = Registry.CurrentUser.OpenSubKey(Root + "microphone");
        if (cam is null && mic is null) return;
        using var camEvent = new AutoResetEvent(false);
        using var micEvent = new AutoResetEvent(false);

        while (true)
        {
            if (cam is not null) Arm(cam, camEvent);
            if (mic is not null) Arm(mic, micEvent);
            var i = WaitHandle.WaitAny([_stop, camEvent, micEvent]);
            if (i == 0) return;
            Thread.Sleep(150); // Windows writes start/stop values in a burst
            Rescan(raise: true);
        }
    }

    private static void Arm(RegistryKey key, AutoResetEvent signal) =>
        RegNotifyChangeKeyValue(key.Handle, true, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET,
            signal.SafeWaitHandle, true);

    private void Rescan(bool raise)
    {
        var cam = InUse("webcam");
        var mic = InUse("microphone");
        _dispatcher.BeginInvoke(() =>
        {
            var changed = !cam.SequenceEqual(CameraApps) || !mic.SequenceEqual(MicrophoneApps);
            CameraApps = cam;
            MicrophoneApps = mic;
            if (changed && raise)
            {
                DiagLog.Write($"Privacy: camera [{string.Join(", ", cam)}] mic [{string.Join(", ", mic)}]");
                Changed?.Invoke();
            }
            else if (changed) Changed?.Invoke();
        });
    }

    private static List<string> InUse(string capability)
    {
        var apps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root + capability);
            if (key is null) return [];
            Collect(key, apps);
            using var desktop = key.OpenSubKey("NonPackaged");
            if (desktop is not null) Collect(desktop, apps);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
        }
        return [.. apps];
    }

    private static void Collect(RegistryKey parent, SortedSet<string> apps)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (name == "NonPackaged") continue;
            using var app = parent.OpenSubKey(name);
            if (app?.GetValue("LastUsedTimeStop") is long stop && stop == 0
                && app.GetValue("LastUsedTimeStart") is long start && start > 0)
                apps.Add(CapabilityApps.Name(name));
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(500);
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        int filter, SafeWaitHandle evt, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
