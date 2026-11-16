using Knob.Core.Devices;

namespace Knob.Core.Tests.Devices;

public class IndicatorTests
{
    [Theory]
    [InlineData(0x041E, "ก ไทย")]
    [InlineData(0x0409, "A EN")]
    [InlineData(0x0809, "A EN")]
    [InlineData(0x0411, "JA")]
    [InlineData(0x0000, null)]
    public void KeyboardLanguage_Labels(int langId, string? expected) => Assert.Equal(expected, KeyboardLanguage.Label(langId));

    [Theory]
    [InlineData(@"C:#Program Files#Zoom#bin#Zoom.exe", "Zoom")]
    [InlineData(@"C:#Users#me#AppData#Local#Discord#app-1.0.9#Discord.exe", "Discord")]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "WindowsCamera")]
    [InlineData("MicrosoftTeams_8wekyb3d8bbwe", "MicrosoftTeams")]
    [InlineData("", "An app")]
    public void ConsentStore_AppNames(string key, string expected) => Assert.Equal(expected, CapabilityApps.Name(key));

    [Fact]
    public void LowAlarm_FiresOnce_UntilRecovered()
    {
        var alarm = new ThresholdAlarm(threshold: 15, rearm: 20, below: true);
        Assert.False(alarm.Check("mouse", 40));
        Assert.True(alarm.Check("mouse", 15));   // crossed
        Assert.False(alarm.Check("mouse", 12));  // already warned
        Assert.False(alarm.Check("mouse", 18));  // not recovered enough
        Assert.False(alarm.Check("mouse", 14));
        Assert.False(alarm.Check("mouse", 25));  // recovered → re-armed
        Assert.True(alarm.Check("mouse", 10));
        Assert.True(alarm.Check("keyboard", 5)); // keys are independent
    }

    [Fact]
    public void HighAlarm_ForChargeLimit()
    {
        var alarm = new ThresholdAlarm(threshold: 80, rearm: 75, below: false);
        Assert.False(alarm.Check("pc", 79));
        Assert.True(alarm.Check("pc", 80));
        Assert.False(alarm.Check("pc", 90));
        alarm.Reset("pc");                        // unplugged
        Assert.True(alarm.Check("pc", 85));
    }

    [Theory]
    [InlineData(80, 15, 80, 15)]
    [InlineData(0, 0, 0, 0)]       // off
    [InlineData(30, 90, 80, 15)]   // nonsense → defaults
    [InlineData(-5, -1, 0, 0)]
    [InlineData(100, 50, 100, 50)]
    public void AlertSettings_Normalize(int charge, int low, int expCharge, int expLow)
    {
        var s = new Knob.Core.Storage.AlertSettings { ChargeLimit = charge, DeviceLowBattery = low };
        s.Normalize();
        Assert.Equal(expCharge, s.ChargeLimit);
        Assert.Equal(expLow, s.DeviceLowBattery);
    }

    [Fact]
    public void Alarm_StartingInZone_FiresOnce()
    {
        var alarm = new ThresholdAlarm(15, 20, below: true);
        Assert.True(alarm.Check("m", 3));
        Assert.False(alarm.Check("m", 3));
    }
}
