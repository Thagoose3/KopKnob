namespace Knob.Core.Monitor;

public static class CpuUsageCalculator
{
    /// <summary>
    /// CPU % from two GetSystemTimes samples. Kernel time already includes idle time.
    /// </summary>
    public static double Compute(long prevIdle, long prevKernel, long prevUser, long idle, long kernel, long user)
    {
        var idleDelta = idle - prevIdle;
        var total = (kernel - prevKernel) + (user - prevUser);
        if (total <= 0) return 0;
        var busy = 100.0 * (total - idleDelta) / total;
        return Math.Clamp(busy, 0, 100);
    }
}
