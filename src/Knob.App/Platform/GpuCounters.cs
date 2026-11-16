using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Knob.App.Platform;

/// <summary>
/// GPU load and dedicated memory via raw PDH — the same counters Task Manager uses:
/// <c>\GPU Engine(*engtype_3D)\Utilization Percentage</c> (summed over all 3D engine instances) and
/// <c>\GPU Adapter Memory(*)\Dedicated Usage</c> (largest adapter). Opened once, sampled only while the System page is shown.
/// </summary>
internal sealed class GpuCounters : IDisposable
{
    private const string UtilPath = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
    private const string MemPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private readonly IntPtr _util, _mem;

    private GpuCounters(IntPtr query, IntPtr util, IntPtr mem)
    {
        _query = query;
        _util = util;
        _mem = mem;
    }

    public static GpuCounters? TryCreate()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out var q) != 0) return null;
            if (PdhAddEnglishCounter(q, UtilPath, IntPtr.Zero, out var u) != 0 ||
                PdhAddEnglishCounter(q, MemPath, IntPtr.Zero, out var m) != 0)
            {
                PdhCloseQuery(q);
                return null;
            }
            PdhCollectQueryData(q); // utilisation is a rate counter: first sample is the baseline
            return new GpuCounters(q, u, m);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Prime()
    {
        if (_query != IntPtr.Zero) PdhCollectQueryData(_query);
    }

    /// <summary>(3D load %, dedicated VRAM in use in bytes); either can be null when unavailable.</summary>
    public (double? Load, double? DedicatedBytes) Sample()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return (null, null);
        var load = Values(_util) is { Count: > 0 } u ? Math.Clamp(u.Sum(), 0, 100) : (double?)null;
        var mem = Values(_mem) is { Count: > 0 } m ? m.Max() : (double?)null;
        return (load, mem);
    }

    /// <summary>All instance values of a wildcard counter (instances come and go with processes; PDH tracks them).</summary>
    private static List<double> Values(IntPtr counter)
    {
        var result = new List<double>();
        uint size = 0;
        var status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return result;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, buffer) != 0)
                return result;
            // PDH_FMT_COUNTERVALUE_ITEM_W { LPWSTR szName; PDH_FMT_COUNTERVALUE { DWORD CStatus; double value } }
            var itemSize = IntPtr.Size + 16;
            for (var i = 0; i < count; i++)
            {
                var item = buffer + i * itemSize;
                var cstatus = (uint)Marshal.ReadInt32(item + IntPtr.Size);
                if (cstatus != 0) continue;
                var bits = Marshal.ReadInt64(item + IntPtr.Size + 8);
                var v = BitConverter.Int64BitsToDouble(bits);
                if (double.IsFinite(v)) result.Add(v);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    /// <summary>Name and dedicated memory of the GPU with the most VRAM (the discrete card, if any), from the registry.</summary>
    public static (string? Name, ulong TotalBytes) ReadAdapterInfo()
    {
        const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        string? bestName = null;
        ulong bestMem = 0;
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(ClassKey);
            if (cls is null) return (null, 0);
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsAsciiDigit)) continue;
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is not string name) continue;
                    ulong mem = k.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long l => (ulong)l,
                        byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                        _ => k.GetValue("HardwareInformation.MemorySize") switch
                        {
                            int i => (uint)i,
                            byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                            _ => 0UL,
                        },
                    };
                    if (bestName is null || mem > bestMem)
                    {
                        bestName = name;
                        bestMem = mem;
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                {
                    // some adapter keys are protected; skip them
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }
        return (bestName, bestMem);
    }
}
