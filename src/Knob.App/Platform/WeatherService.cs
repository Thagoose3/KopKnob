using System.Net.Http;
using Knob.Core.Storage;
using Knob.Core.Weather;

namespace Knob.App.Platform;

/// <summary>
/// Weather for the place in settings, from Open-Meteo (free, no key). Fetched at most every 30 minutes, driven
/// by the pill's minute tick, so there is no timer of its own. Only the rounded coordinates (or the typed city
/// name, when searching) are sent.
/// </summary>
public sealed class WeatherService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly WeatherSettings _settings;
    private DateTime _fetchedUtc = DateTime.MinValue;
    private bool _busy;

    public WeatherService(WeatherSettings settings) => _settings = settings;

    public WeatherReport? Report { get; private set; }
    /// <summary>Short reason the last fetch failed; null after a success.</summary>
    public string? Error { get; private set; }
    public bool Busy => _busy;
    public WeatherSettings Settings => _settings;

    /// <summary>Raised on the calling (UI) thread when Report/Error/Busy change.</summary>
    public event Action? Changed;

    public async Task RefreshAsync(bool force = false)
    {
        if (!_settings.HasPlace || _busy) return;
        if (!force && DateTime.UtcNow - _fetchedUtc < MaxAge) return;

        _busy = true;
        Changed?.Invoke();
        try
        {
            var json = await Http.GetStringAsync(OpenMeteo.ForecastUrl(_settings.Latitude!.Value, _settings.Longitude!.Value));
            Report = WeatherReport.Parse(json);
            Error = null;
            _fetchedUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
        {
            Error = ex is FormatException ? "Unexpected reply from the weather service" : "Offline — couldn't reach the weather service";
            _fetchedUtc = DateTime.UtcNow - MaxAge + TimeSpan.FromMinutes(5); // retry in ~5 minutes
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Look up places by name; empty on no match or no network.</summary>
    public static async Task<IReadOnlyList<GeoPlace>> SearchAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        try
        {
            return GeoPlace.ParseSearch(await Http.GetStringAsync(OpenMeteo.SearchUrl(name)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    public void SetPlace(GeoPlace place)
    {
        _settings.City = place.Label;
        _settings.Latitude = Math.Round(place.Latitude, 2);
        _settings.Longitude = Math.Round(place.Longitude, 2);
        Report = null;
        Error = null;
        _fetchedUtc = DateTime.MinValue;
        Changed?.Invoke();
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Knob/1.0");
        return c;
    }
}
