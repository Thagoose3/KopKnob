using System.Text.Json.Serialization;
using Knob.Core.Storage;

namespace Knob.Core.Shelf;

/// <summary>
/// An ordered list of file/folder paths without duplicates (case-insensitive, as on Windows).
/// Used by the file shelf and the quick-launch pins; saved as JSON. Holds references only — never copies files.
/// </summary>
public sealed class PathList : IJsonNormalizable
{
    public List<string> Paths { get; set; } = [];

    /// <summary>Oldest entries are dropped beyond this many.</summary>
    [JsonIgnore]
    public int Max { get; init; } = 200;

    /// <summary>Append paths that aren't there yet; returns how many were added.</summary>
    public int Add(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || Contains(p)) continue;
            Paths.Add(p);
            added++;
        }
        while (Paths.Count > Max) Paths.RemoveAt(0);
        return added;
    }

    public bool Contains(string path) => Paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    public bool Remove(string path) =>
        Paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>Remove entries for which <paramref name="exists"/> is false; returns how many went.</summary>
    public int Prune(Func<string, bool> exists) => Paths.RemoveAll(p => !exists(p));

    /// <summary>Move one place earlier (−1) or later (+1). False at either end or for an unknown path.</summary>
    public bool Move(string path, int delta)
    {
        var i = Paths.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        var j = i + Math.Sign(delta);
        if (i < 0 || delta == 0 || j < 0 || j >= Paths.Count) return false;
        (Paths[i], Paths[j]) = (Paths[j], Paths[i]);
        return true;
    }

    public void Normalize()
    {
        var old = Paths ?? [];
        Paths = [];
        Add(old.Where(p => p is not null));
    }
}
