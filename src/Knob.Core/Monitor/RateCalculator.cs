using Knob.Core.Storage;

namespace Knob.Core.Monitor;

/// <summary>Bytes/second from a growing counter. A counter that goes backwards (adapter reset) yields 0.</summary>
public sealed class RateCalculator
{
    private long? _last;
    private DateTimeOffset _lastAt;

    public double Update(long counter, DateTimeOffset at)
    {
        var prev = _last;
        var prevAt = _lastAt;
        _last = counter;
        _lastAt = at;

        if (prev is null) return 0;
        var seconds = (at - prevAt).TotalSeconds;
        var delta = counter - prev.Value;
        if (seconds <= 0 || delta < 0) return 0;
        return delta / seconds;
    }

    public void Reset() => _last = null;

    public static string Format(double bytesPerSecond) =>
        ByteFormatter.Format((long)Math.Round(Math.Max(0, bytesPerSecond))) + "/s";
}
