using Knob.Core.Devices;

namespace Knob.Core.Tests.Devices;

public class DevicesTests
{
    [Theory]
    [InlineData(@"BTHLE\DEV_C0E4AB12CD34\7&2A1B3C4D&0&C0E4AB12CD34", "C0E4AB12CD34")]
    [InlineData(@"BTHENUM\DEV_001A7DDA7113\7&1F2E&0&BLUETOOTHDEVICE_001A7DDA7113", "001A7DDA7113")]
    [InlineData(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_LOCALMFG&005D\7&2A1B&0&001A7DDA7113_C00000000", "001A7DDA7113")]
    [InlineData("BluetoothLE#BluetoothLE10:2b:41:aa:bb:cc-c0:e4:ab:12:cd:34", "C0E4AB12CD34")]
    [InlineData("Bluetooth#Bluetooth10:2b:41:aa:bb:cc-00:1a:7d:da:71:13", "001A7DDA7113")]
    [InlineData(@"USB\VID_046D&PID_C52B\5&1234", null)]
    [InlineData("", null)]
    public void Address_IsExtracted(string id, string? expected) => Assert.Equal(expected, BluetoothAddress.FromId(id));

    [Theory]
    [InlineData(0.50f, 120, 0.52f)]
    [InlineData(0.50f, -120, 0.48f)]
    [InlineData(0.99f, 240, 1.00f)]
    [InlineData(0.01f, -360, 0.00f)]
    [InlineData(0.50f, 60, 0.51f)]   // precision touchpads send small deltas
    public void VolumeStep_FollowsWheel(float start, int delta, float expected) =>
        Assert.Equal(expected, VolumeStep.Apply(start, delta), 3);

    [Theory]
    [InlineData(0.004f, 0)]
    [InlineData(0.456f, 46)]
    [InlineData(1f, 100)]
    public void Percent_Rounds(float level, int expected) => Assert.Equal(expected, VolumeStep.Percent(level));
}
