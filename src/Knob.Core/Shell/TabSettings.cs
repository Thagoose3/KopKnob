namespace Knob.Core.Shell;

/// <summary>
/// Which panel pages show in the bottom tab bar, in what order, and which one was open last.
/// Stored in settings.json under "Tabs". Unknown ids are dropped, new pages are appended,
/// and at least one tab always stays visible.
/// </summary>
public sealed class TabSettings
{
    public static readonly string[] DefaultOrder =
        ["home", "music", "timer", "clipboard", "notes", "todo", "calendar", "calc", "qr", "system"];

    public List<string> Order { get; set; } = [.. DefaultOrder];
    public List<string> Hidden { get; set; } = [];
    public string? Last { get; set; }

    public IReadOnlyList<string> Visible => Order.Where(id => !Hidden.Contains(id)).ToList();

    public void Normalize(IReadOnlyList<string> known)
    {
        var order = new List<string>();
        foreach (var id in Order ?? [])
            if (id is not null && known.Contains(id) && !order.Contains(id)) order.Add(id);
        foreach (var id in known)
            if (!order.Contains(id)) order.Add(id);
        Order = order;

        Hidden = (Hidden ?? []).Where(id => id is not null && order.Contains(id)).Distinct().ToList();
        if (Visible.Count == 0 && Order.Count > 0) Hidden.Remove(Order[0]);

        FixLast();
    }

    /// <summary>Move a tab one place left (−1) or right (+1). False at either end or for an unknown id.</summary>
    public bool Move(string id, int delta)
    {
        var i = Order.IndexOf(id);
        var j = i + Math.Sign(delta);
        if (i < 0 || delta == 0 || j < 0 || j >= Order.Count) return false;
        (Order[i], Order[j]) = (Order[j], Order[i]);
        return true;
    }

    /// <summary>Show or hide a tab. Refuses to hide the last visible one.</summary>
    public bool SetHidden(string id, bool hidden)
    {
        if (!Order.Contains(id)) return false;
        if (hidden)
        {
            if (Hidden.Contains(id)) return true;
            if (Visible.Count <= 1) return false;
            Hidden.Add(id);
        }
        else
        {
            Hidden.Remove(id);
        }
        FixLast();
        return true;
    }

    private void FixLast()
    {
        var visible = Visible;
        if (Last is null || !visible.Contains(Last)) Last = visible.Count > 0 ? visible[0] : null;
    }
}
