using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Knob.App.Platform;

/// <summary>Raw counters for the system monitor. No PerformanceCounter (slow to init, heavy).</summary>
internal static class SystemMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    public static bool TryGetSystemTimes(out long idle, out long kernel, out long user) =>
        GetSystemTimes(out idle, out kernel, out user);

    /// <summary>(percent used, used bytes, total bytes)</summary>
    public static (int Percent, ulong Used, ulong Total) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
        return ((int)m.dwMemoryLoad, m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys);
    }

    /// <summary>Total bytes received/sent over all interfaces that are up and not loopback/tunnel.</summary>
    public static (long Received, long Sent) NetworkTotals()
    {
        long rx = 0, tx = 0;
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return (0, 0); }

        foreach (var nic in nics)
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = nic.GetIPv4Statistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            catch (Exception)
            {
                // adapter vanished mid-read
            }
        }
        return (rx, tx);
    }
}
