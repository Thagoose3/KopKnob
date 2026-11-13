using System.Runtime.InteropServices;

namespace Knob.App.Platform;

/// <summary>
/// "% Processor Utility" via raw PDH — the frequency-aware number Task Manager shows (time-based
/// GetSystemTimes reads ~half of it when the CPU turbo-boosts). Not System.Diagnostics.PerformanceCounter.
/// Opened once, sampled only while the monitor is visible.
/// </summary>
internal sealed class CpuUtilityCounter : IDisposable
{
    private const string CounterPath = @"\Processor Information(_Total)\% Processor Utility";
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private readonly IntPtr _counter;

    private CpuUtilityCounter(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    /// <summary>null if PDH or the counter is unavailable (caller falls back to GetSystemTimes).</summary>
    public static CpuUtilityCounter? TryCreate()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out var q) != 0) return null;
            if (PdhAddEnglishCounter(q, CounterPath, IntPtr.Zero, out var c) != 0)
            {
                PdhCloseQuery(q);
                return null;
            }
            PdhCollectQueryData(q); // rate counter: first sample is the baseline
            return new CpuUtilityCounter(q, c);
        }
        catch (Exception) // pdh.dll missing / entry point not found
        {
            return null;
        }
    }

    /// <summary>Re-baseline after a pause so the first reading isn't an average over the idle gap.</summary>
    public void Prime()
    {
        if (_query != IntPtr.Zero) PdhCollectQueryData(_query);
    }

    /// <summary>Utility % since the previous call, capped to 0–100 like Task Manager; null on failure.</summary>
    public double? Sample()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return null;
        if (PdhGetFormattedCounterValue(_counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) != 0 || v.CStatus != 0)
            return null;
        return Math.Clamp(v.doubleValue, 0, 100);
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }
}
