using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Knob.Core.Storage;
using Knob.Core.Update;

namespace Knob.App.Platform;

/// <summary>
/// New versions come from the project's GitHub releases (an ordinary HTTPS request — no account, no personal data).
/// Checks at most about once a day, driven by the pill's minute tick. Installing downloads KnobSetup-x.y.z.exe to a
/// temp folder, checks its size and GitHub's SHA-256, and runs it silently: the installer closes this Knob,
/// updates the files in place (notes and settings are kept) and starts the new one.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "Thagoose3/KopKnob";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    private static readonly HttpClient Http = CreateClient();

    private readonly UpdateSettings _settings;
    private readonly Action _save;

    public UpdateService(UpdateSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    public static Version Current
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public UpdateSettings Settings => _settings;
    /// <summary>A newer release, once a check found one.</summary>
    public ReleaseInfo? Available { get; private set; }
    public bool Busy { get; private set; }
    /// <summary>Short status for Settings → About ("Up to date", "Downloading 40%", an error).</summary>
    public string Status { get; private set; } = "";

    /// <summary>Raised (on the UI thread) when Available/Busy/Status change.</summary>
    public event Action? Changed;
    /// <summary>Raised once per new version, so the pill can mention it.</summary>
    public event Action<ReleaseInfo>? NewVersionFound;

    /// <summary>Called every minute: checks when automatic checks are on and the last one is a day old.</summary>
    public void Tick()
    {
        if (!_settings.AutoCheck || Busy) return;
        if (_settings.LastCheck is { } last && DateTimeOffset.Now - last < CheckEvery) return;
        _ = CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (Busy) return;
        Set(busy: true, "Checking…");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request);
            _settings.LastCheck = DateTimeOffset.Now;
            _save();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Available = null;
                Set(busy: false, "Up to date");
                return;
            }
            response.EnsureSuccessStatusCode();
            var release = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync());
            Available = release is not null && ReleaseInfo.IsNewer(Current, release.Version) ? release : null;
            Set(busy: false, Available is null ? "Up to date" : $"Version {Available.Version.ToString(3)} is available");

            if (Available is not null && _settings.AnnouncedVersion != Available.Version.ToString(3))
            {
                _settings.AnnouncedVersion = Available.Version.ToString(3);
                _save();
                NewVersionFound?.Invoke(Available);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Set(busy: false, "Couldn't reach GitHub — try again later");
        }
    }

    /// <summary>Download, verify and run the installer. Returns false (with Status set) when it can't.</summary>
    /// <param name="beforeInstall">Save anything unsaved; the installer closes Knob.</param>
    public async Task<bool> InstallAsync(Action beforeInstall)
    {
        if (Busy || Available is not ReleaseInfo release) return false;
        Set(busy: true, "Downloading…");
        var dir = Path.Combine(Path.GetTempPath(), "Knob-update");
        var file = Path.Combine(dir, release.AssetName);
        try
        {
            Directory.CreateDirectory(dir);
            using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? release.Size;
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(file);
                var buffer = new byte[81920];
                long done = 0;
                int read, lastPct = -1;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    var pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != lastPct && pct % 5 == 0) { lastPct = pct; Set(busy: true, $"Downloading… {pct}%"); }
                }
            }

            Set(busy: true, "Checking the download…");
            var info = new FileInfo(file);
            if (release.Size > 0 && info.Length != release.Size) return Fail("The download was incomplete — try again");
            if (release.Sha256 is string expected)
            {
                await using var fs = File.OpenRead(file);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
                if (actual != expected) return Fail("The download didn't match GitHub's checksum — not installed");
            }

            Set(busy: true, "Installing… Knob will restart");
            beforeInstall();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, "/S") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Fail("Update failed: " + ex.Message);
        }

        bool Fail(string message)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Set(busy: false, message);
            return false;
        }
    }

    private void Set(bool busy, string status)
    {
        Busy = busy;
        Status = status;
        Changed?.Invoke();
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Knob/{Current.ToString(3)}");
        return c;
    }
}
