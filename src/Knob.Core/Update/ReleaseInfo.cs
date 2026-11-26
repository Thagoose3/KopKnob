using System.Text.Json;
using System.Text.RegularExpressions;

namespace Knob.Core.Update;

/// <summary>
/// The latest GitHub release as far as updating is concerned: its version (from the tag, "v1.2.0"), the attached
/// KnobSetup-*.exe and GitHub's SHA-256 of it. Drafts, pre-releases and releases without an installer are ignored.
/// </summary>
public sealed partial record ReleaseInfo(
    Version Version,
    string AssetName,
    string AssetUrl,
    long Size,
    string? Sha256,
    string? PageUrl,
    string? Notes)
{
    /// <summary>Parse a "GET /repos/{owner}/{repo}/releases/latest" response; null when there is nothing to install.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (Bool(r, "draft") || Bool(r, "prerelease")) return null;
            if (!r.TryGetProperty("tag_name", out var tag) || ParseTag(tag.GetString()) is not Version version) return null;
            if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!InstallerName().IsMatch(name)) continue;
                var url = a.GetProperty("browser_download_url").GetString();
                if (string.IsNullOrEmpty(url)) continue;
                string? sha = null;
                if (a.TryGetProperty("digest", out var d) && d.GetString() is string digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest[7..].ToLowerInvariant();
                return new ReleaseInfo(
                    version, name, url,
                    a.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                    sha,
                    r.TryGetProperty("html_url", out var page) ? page.GetString() : null,
                    r.TryGetProperty("body", out var body) ? body.GetString() : null);
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>"v1.2.0" / "1.2" → 1.2.0; anything else → null.</summary>
    public static Version? ParseTag(string? tag)
    {
        var m = TagPattern().Match(tag ?? "");
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    /// <summary>Compares major.minor.build only (assembly versions carry a 4th part).</summary>
    public static bool IsNewer(Version current, Version latest) =>
        new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build)) >
        new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^KnobSetup.*\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerName();
}
