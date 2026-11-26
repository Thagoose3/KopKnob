namespace Knob.Core.Search;

/// <summary>
/// Ranking for the quick search palette: exact title &gt; title prefix &gt; a word in the title starts with it &gt;
/// title contains it &gt; keywords contain it &gt; the letters appear in order (fuzzy). Case-insensitive.
/// Ties: shorter title first, then the original order.
/// </summary>
public static class PaletteSearch
{
    public static IReadOnlyList<T> Rank<T>(string query, IEnumerable<T> items, Func<T, string> title, Func<T, string?>? keywords = null)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) return [];
        return items
            .Select((item, index) => (item, index, title: title(item), score: Score(q, title(item).ToLowerInvariant(), keywords?.Invoke(item)?.ToLowerInvariant())))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.title.Length)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();
    }

    private static int Score(string q, string title, string? keywords)
    {
        if (title == q) return 100;
        if (title.StartsWith(q, StringComparison.Ordinal)) return 80;
        if (title.Split([' ', '-', '_', '.', '/'], StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 60;
        if (title.Contains(q, StringComparison.Ordinal)) return 40;
        if (keywords is not null && keywords.Contains(q, StringComparison.Ordinal)) return 30;
        return IsSubsequence(q, title) ? 10 : 0;
    }

    private static bool IsSubsequence(string q, string s)
    {
        var i = 0;
        foreach (var c in s)
            if (i < q.Length && c == q[i]) i++;
        return i == q.Length;
    }

    /// <summary>True for something the calculator should answer ("12*7", "1,250 + 20%"), not a bare number or a word.</summary>
    public static bool LooksLikeMath(string query)
    {
        var q = query.Trim();
        if (q.Length < 2 || !q.Any(char.IsAsciiDigit)) return false;
        if (q.Any(c => !(char.IsAsciiDigit(c) || " .,+-*/×÷%^()".Contains(c)))) return false;
        if (!q.Skip(1).Any(c => "+-*/×÷%^".Contains(c)) && !q.Contains('(')) return false; // needs an operator
        return Calc.ExpressionEvaluator.TryEvaluate(q, out _, out _);
    }
}
