namespace Knob.Core.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(7));
    public void Advance(TimeSpan by) => Now += by;
}
