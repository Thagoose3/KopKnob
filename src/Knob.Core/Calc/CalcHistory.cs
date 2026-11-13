using System.Globalization;
using Knob.Core.Storage;

namespace Knob.Core.Calc;

public sealed class CalcEntry
{
    public string Expression { get; set; } = "";
    public string Result { get; set; } = "";
}

/// <summary>Last 20 calculations, newest first. Serialized as calc-history.json.</summary>
public sealed class CalcHistory : IJsonNormalizable
{
    public const int MaxEntries = 20;

    public List<CalcEntry> Entries { get; set; } = [];

    public void Add(string expression, string result)
    {
        if (string.IsNullOrWhiteSpace(expression)) return;
        expression = expression.Trim();
        if (Entries.Count > 0 && Entries[0].Expression == expression && Entries[0].Result == result) return;
        Entries.Insert(0, new CalcEntry { Expression = expression, Result = result });
        if (Entries.Count > MaxEntries) Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
    }

    public void Normalize()
    {
        Entries ??= [];
        Entries.RemoveAll(e => e is null || string.IsNullOrWhiteSpace(e.Expression) || e.Result is null);
        if (Entries.Count > MaxEntries) Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
    }

    /// <summary>Thousands separators, no trailing zeros, invariant culture: 1234567.50 → "1,234,567.5".</summary>
    public static string Format(decimal value) =>
        value.ToString("#,##0.############################", CultureInfo.InvariantCulture);
}
