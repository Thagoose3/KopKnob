using Knob.Core.Weather;

namespace Knob.Core.Tests.Weather;

public class WeatherTests
{
    private const string Forecast = """
    {
      "current": { "time": "2026-11-16T11:00", "temperature_2m": 31.4, "apparent_temperature": 36.2,
                   "relative_humidity_2m": 66, "wind_speed_10m": 9.7, "weather_code": 2, "is_day": 1 },
      "hourly": {
        "time": ["2026-11-16T09:00","2026-11-16T10:00","2026-11-16T11:00","2026-11-16T12:00","2026-11-16T13:00",
                 "2026-11-16T14:00","2026-11-16T15:00","2026-11-16T16:00","2026-11-16T17:00"],
        "temperature_2m": [27.0, 29.1, 31.4, 32.2, 33.0, 32.5, 31.0, 30.2, 29.0],
        "weather_code":   [0, 1, 2, 3, 61, 95, 3, 2, 1],
        "is_day":         [1, 1, 1, 1, 1, 1, 1, 1, 1]
      },
      "daily": { "temperature_2m_max": [33.6, 32.0], "temperature_2m_min": [24.8, 25.1], "weather_code": [95, 3] }
    }
    """;

    [Fact]
    public void Parse_ReadsCurrent_Today_AndHoursFromNow()
    {
        var r = WeatherReport.Parse(Forecast, new DateTime(2026, 11, 16, 11, 25, 0));

        Assert.Equal(31.4, r.Temperature);
        Assert.Equal(36.2, r.FeelsLike);
        Assert.Equal(66, r.Humidity);
        Assert.Equal(9.7, r.WindKmh);
        Assert.Equal(WeatherKind.PartlyCloudy, r.Kind);
        Assert.True(r.IsDay);
        Assert.Equal(33.6, r.TodayMax);
        Assert.Equal(24.8, r.TodayMin);

        // Starts at the current hour, six entries
        Assert.Equal(6, r.Hours.Count);
        Assert.Equal(12, r.Hours[0].Time.Hour);
        Assert.Equal(WeatherKind.Overcast, r.Hours[0].Kind);
        Assert.Equal(WeatherKind.Rain, r.Hours[1].Kind);
        Assert.Equal(WeatherKind.Thunder, r.Hours[2].Kind);
    }

    [Fact]
    public void Parse_Throws_FormatException_OnGarbage()
    {
        Assert.Throws<FormatException>(() => WeatherReport.Parse("{\"nope\":1}", DateTime.Now));
        Assert.Throws<FormatException>(() => WeatherReport.Parse("not json", DateTime.Now));
    }

    [Theory]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(1, WeatherKind.PartlyCloudy)]
    [InlineData(3, WeatherKind.Overcast)]
    [InlineData(45, WeatherKind.Fog)]
    [InlineData(53, WeatherKind.Drizzle)]
    [InlineData(63, WeatherKind.Rain)]
    [InlineData(81, WeatherKind.Rain)]
    [InlineData(73, WeatherKind.Snow)]
    [InlineData(96, WeatherKind.Thunder)]
    [InlineData(1234, WeatherKind.Unknown)]
    public void Codes_MapToKinds(int code, WeatherKind kind) => Assert.Equal(kind, WeatherCodes.Kind(code));

    [Fact]
    public void Codes_HaveText() => Assert.Equal("Thunderstorm", WeatherCodes.Text(95));

    [Fact]
    public void SearchResults_Parse()
    {
        const string json = """
        { "results": [
            { "name": "Bangkok", "latitude": 13.75398, "longitude": 100.50144, "country": "Thailand", "admin1": "Bangkok" },
            { "name": "Bang Kok", "latitude": 7.1, "longitude": 99.2, "country": "Thailand" } ] }
        """;
        var places = GeoPlace.ParseSearch(json);
        Assert.Equal(2, places.Count);
        Assert.Equal("Bangkok", places[0].Name);
        Assert.Equal("Bangkok, Thailand", places[0].Label);
        Assert.Equal("Bang Kok, Thailand", places[1].Label);
        Assert.Equal(13.75398, places[0].Latitude);

        Assert.Empty(GeoPlace.ParseSearch("{\"generationtime_ms\":0.3}"));
    }

    [Fact]
    public void Settings_DropOutOfRangePlaces()
    {
        var ok = new Knob.Core.Storage.WeatherSettings { City = "Bangkok, Thailand", Latitude = 13.75, Longitude = 100.5 };
        ok.Normalize();
        Assert.True(ok.HasPlace);

        var bad = new Knob.Core.Storage.WeatherSettings { City = "X", Latitude = 123, Longitude = 100.5 };
        bad.Normalize();
        Assert.False(bad.HasPlace);
        Assert.Null(bad.City);

        Assert.False(new Knob.Core.Storage.WeatherSettings().HasPlace);
    }

    [Fact]
    public void Urls_RoundCoordinates_AndEscapeNames()
    {
        var url = OpenMeteo.ForecastUrl(13.75398, 100.50144);
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?latitude=13.75&longitude=100.5&", url);
        Assert.Contains("timezone=auto", url);

        Assert.Equal("https://geocoding-api.open-meteo.com/v1/search?name=Chiang%20Mai&count=5&language=en&format=json",
            OpenMeteo.SearchUrl("Chiang Mai"));
    }
}
