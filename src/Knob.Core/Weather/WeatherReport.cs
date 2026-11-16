using System.Globalization;
using System.Text.Json;

namespace Knob.Core.Weather;

public enum WeatherKind { Unknown, Clear, PartlyCloudy, Overcast, Fog, Drizzle, Rain, Snow, Thunder }

/// <summary>WMO weather interpretation codes (as used by Open-Meteo) → a coarse kind and a short English label.</summary>
public static class WeatherCodes
{
    public static WeatherKind Kind(int code) => code switch
    {
        0 => WeatherKind.Clear,
        1 or 2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Overcast,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => WeatherKind.Rain,
        (>= 71 and <= 77) or 85 or 86 => WeatherKind.Snow,
        >= 95 and <= 99 => WeatherKind.Thunder,
        _ => WeatherKind.Unknown,
    };

    public static string Text(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 => "Rain showers",
        82 => "Heavy showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm, hail",
        _ => "—",
    };
}

public sealed record HourForecast(DateTime Time, double Temperature, WeatherKind Kind, bool IsDay);

/// <summary>Current conditions, today's range and the next few hours, parsed from an Open-Meteo forecast response.</summary>
public sealed record WeatherReport(
    double Temperature,
    double FeelsLike,
    int Humidity,
    double WindKmh,
    int Code,
    bool IsDay,
    double TodayMax,
    double TodayMin,
    IReadOnlyList<HourForecast> Hours)
{
    public WeatherKind Kind => WeatherCodes.Kind(Code);
    public string Text => WeatherCodes.Text(Code);

    /// <summary>
    /// Parse a forecast response. Hours after <paramref name="now"/> (local time at the place, timezone=auto) are
    /// listed; without it, the response's own "current.time" is used.
    /// </summary>
    /// <exception cref="FormatException">The response is not a forecast.</exception>
    public static WeatherReport Parse(string json, DateTime? now = null, int hours = 6)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cur = root.GetProperty("current");
            var placeNow = now ?? DateTime.Parse(cur.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
            var daily = root.GetProperty("daily");
            var hourly = root.GetProperty("hourly");

            var list = new List<HourForecast>();
            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var codes = hourly.GetProperty("weather_code");
            hourly.TryGetProperty("is_day", out var days);
            for (var i = 0; i < times.GetArrayLength() && list.Count < hours; i++)
            {
                var t = DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture);
                if (t <= placeNow) continue;
                var isDay = days.ValueKind != JsonValueKind.Array || days[i].GetInt32() == 1;
                list.Add(new HourForecast(t, temps[i].GetDouble(), WeatherCodes.Kind(codes[i].GetInt32()), isDay));
            }

            return new WeatherReport(
                cur.GetProperty("temperature_2m").GetDouble(),
                cur.GetProperty("apparent_temperature").GetDouble(),
                (int)Math.Round(cur.GetProperty("relative_humidity_2m").GetDouble()),
                cur.GetProperty("wind_speed_10m").GetDouble(),
                cur.GetProperty("weather_code").GetInt32(),
                cur.GetProperty("is_day").GetInt32() == 1,
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                list);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
                                       or IndexOutOfRangeException or FormatException or ArgumentNullException)
        {
            throw new FormatException("Not a forecast response.", ex);
        }
    }
}

/// <summary>A place from the geocoding search.</summary>
public sealed record GeoPlace(string Name, string? Country, double Latitude, double Longitude)
{
    public string Label => string.IsNullOrEmpty(Country) ? Name : $"{Name}, {Country}";

    public static IReadOnlyList<GeoPlace> ParseSearch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)) return [];
            var list = new List<GeoPlace>();
            foreach (var r in results.EnumerateArray())
            {
                var country = r.TryGetProperty("country", out var c) ? c.GetString() : null;
                list.Add(new GeoPlace(r.GetProperty("name").GetString() ?? "?", country,
                    r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }
}

/// <summary>Open-Meteo endpoints (free, no API key). Coordinates are rounded to ~1 km before they leave the PC.</summary>
public static class OpenMeteo
{
    public static string ForecastUrl(double latitude, double longitude) =>
        "https://api.open-meteo.com/v1/forecast" +
        $"?latitude={Round(latitude)}&longitude={Round(longitude)}" +
        "&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code,is_day" +
        "&hourly=temperature_2m,weather_code,is_day" +
        "&daily=temperature_2m_max,temperature_2m_min,weather_code" +
        "&timezone=auto&forecast_days=2";

    public static string SearchUrl(string name) =>
        $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name.Trim())}&count=5&language=en&format=json";

    private static string Round(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
