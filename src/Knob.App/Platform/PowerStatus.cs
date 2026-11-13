using System.Windows.Forms;

namespace Knob.App.Platform;

internal static class PowerStatus
{
    /// <summary>Battery percent 0–100, or null when there is no battery / status unknown.</summary>
    public static int? BatteryPercent()
    {
        try
        {
            var s = SystemInformation.PowerStatus;
            if (s.BatteryChargeStatus == BatteryChargeStatus.Unknown ||
                s.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
                return null;

            var p = s.BatteryLifePercent;
            if (p < 0f || p > 1f) return null;
            return (int)Math.Round(p * 100);
        }
        catch
        {
            return null;
        }
    }
}
