using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Crest.Core;

namespace Crest.Services
{
    public sealed class Place
    {
        public string Name { get; set; } = "";
        public string Detail { get; set; } = "";
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public sealed class HourWeather { public string Time { get; set; } = ""; public string Symbol { get; set; } = ""; public string Temp { get; set; } = ""; }
    public sealed class DayWeather { public string Day { get; set; } = ""; public string Symbol { get; set; } = ""; public double Min { get; set; } public double Max { get; set; } }

    public sealed class WeatherData
    {
        public double Temp, Feels, Wind;
        public int Humidity, Code;
        public bool IsDay = true;
        public string Sunrise = "", Sunset = "";
        public List<HourWeather> Hours = new List<HourWeather>();
        public List<DayWeather> Days = new List<DayWeather>();
        public DateTime Fetched;
        public string Unit = "C";
    }

    /// <summary>Weather from Open-Meteo (free, no API key, no account).</summary>
    public sealed class WeatherService
    {
        public static WeatherService Instance { get; } = new WeatherService();
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        bool _busy;

        public WeatherData? Data { get; private set; }
        public string? Error { get; private set; }
        public event Action? Updated;

        static WeatherService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Crest/1.0 (+https://github.com/rishii-hub/crest)");
        }

        public bool HasLocation => SettingsStore.Current.Lat.HasValue && SettingsStore.Current.Lon.HasValue;

        public async Task RefreshIfStaleAsync()
        {
            if (Data == null || DateTime.Now - Data.Fetched > TimeSpan.FromMinutes(15) || Data.Unit != SettingsStore.Current.TempUnit)
                await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            var s = SettingsStore.Current;
            if (!HasLocation || _busy) return;
            _busy = true;
            try
            {
                var unit = s.TempUnit == "F" ? "fahrenheit" : "celsius";
                var wind = s.TempUnit == "F" ? "mph" : "kmh";
                var url = "https://api.open-meteo.com/v1/forecast?latitude=" + s.Lat!.Value.ToString(Inv) + "&longitude=" + s.Lon!.Value.ToString(Inv) +
                          "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
                          "&hourly=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset" +
                          "&timezone=auto&forecast_days=6&temperature_unit=" + unit + "&wind_speed_unit=" + wind;
                var json = await Http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var cur = root.GetProperty("current");
                var d = new WeatherData
                {
                    Temp = cur.GetProperty("temperature_2m").GetDouble(),
                    Feels = cur.GetProperty("apparent_temperature").GetDouble(),
                    Humidity = (int)Math.Round(cur.GetProperty("relative_humidity_2m").GetDouble()),
                    Code = cur.GetProperty("weather_code").GetInt32(),
                    Wind = cur.GetProperty("wind_speed_10m").GetDouble(),
                    IsDay = cur.GetProperty("is_day").GetInt32() == 1,
                    Fetched = DateTime.Now,
                    Unit = s.TempUnit
                };
                var curTime = cur.GetProperty("time").GetString() ?? "";

                var hourly = root.GetProperty("hourly");
                var times = hourly.GetProperty("time");
                var temps = hourly.GetProperty("temperature_2m");
                var codes = hourly.GetProperty("weather_code");
                var days = hourly.GetProperty("is_day");
                int start = 0;
                for (int i = 0; i < times.GetArrayLength(); i++)
                {
                    if (string.CompareOrdinal(times[i].GetString(), curTime) <= 0) start = i; else break;
                }
                for (int i = start; i < Math.Min(start + 12, times.GetArrayLength()); i++)
                {
                    var t = DateTime.Parse(times[i].GetString()!, Inv);
                    d.Hours.Add(new HourWeather
                    {
                        Time = i == start ? "Now" : t.ToString("h tt", CultureInfo.CurrentCulture).ToLowerInvariant(),
                        Symbol = Symbol(codes[i].GetInt32(), days[i].GetInt32() == 1),
                        Temp = $"{Math.Round(temps[i].GetDouble())}°"
                    });
                }

                var daily = root.GetProperty("daily");
                var dt = daily.GetProperty("time");
                for (int i = 0; i < dt.GetArrayLength(); i++)
                {
                    var day = DateTime.Parse(dt[i].GetString()!, Inv);
                    d.Days.Add(new DayWeather
                    {
                        Day = i == 0 ? "Today" : day.ToString("ddd", CultureInfo.CurrentCulture),
                        Symbol = Symbol(daily.GetProperty("weather_code")[i].GetInt32(), true),
                        Min = daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                        Max = daily.GetProperty("temperature_2m_max")[i].GetDouble()
                    });
                }
                if (daily.GetProperty("sunrise").GetArrayLength() > 0)
                {
                    d.Sunrise = DateTime.Parse(daily.GetProperty("sunrise")[0].GetString()!, Inv).ToString("t", CultureInfo.CurrentCulture);
                    d.Sunset = DateTime.Parse(daily.GetProperty("sunset")[0].GetString()!, Inv).ToString("t", CultureInfo.CurrentCulture);
                }
                Data = d;
                Error = null;
            }
            catch (Exception ex)
            {
                Error = "Couldn't reach Open-Meteo. Check your connection.";
                Log.Error("weather", ex);
            }
            finally { _busy = false; }
            Updated?.Invoke();
        }

        public static async Task<List<Place>> SearchAsync(string query)
        {
            var list = new List<Place>();
            if (string.IsNullOrWhiteSpace(query)) return list;
            var url = "https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name=" + Uri.EscapeDataString(query.Trim());
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
            foreach (var r in results.EnumerateArray())
            {
                var parts = new List<string>();
                if (r.TryGetProperty("admin1", out var a) && a.ValueKind == JsonValueKind.String) parts.Add(a.GetString()!);
                if (r.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.String) parts.Add(c.GetString()!);
                list.Add(new Place
                {
                    Name = r.GetProperty("name").GetString() ?? "",
                    Detail = string.Join(", ", parts),
                    Lat = r.GetProperty("latitude").GetDouble(),
                    Lon = r.GetProperty("longitude").GetDouble()
                });
            }
            return list;
        }

        public static async Task<Place?> LocateAsync()
        {
            try
            {
                var access = await global::Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
                if (access != global::Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed) return null;
                var geo = new global::Windows.Devices.Geolocation.Geolocator();
                var task = geo.GetGeopositionAsync().AsTask();
                if (await Task.WhenAny(task, Task.Delay(10000)) != task) return null;
                var p = task.Result.Coordinate.Point.Position;
                return new Place { Name = "Current location", Lat = p.Latitude, Lon = p.Longitude };
            }
            catch (Exception ex) { Log.Error("locate", ex); return null; }
        }

        public static string Describe(int code) => code switch
        {
            0 => "Clear",
            1 => "Mostly clear",
            2 => "Partly cloudy",
            3 => "Cloudy",
            45 or 48 => "Fog",
            51 or 53 or 55 => "Drizzle",
            56 or 57 => "Freezing drizzle",
            61 => "Light rain",
            63 => "Rain",
            65 => "Heavy rain",
            66 or 67 => "Freezing rain",
            71 or 73 or 75 or 77 => "Snow",
            80 or 81 or 82 => "Showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm, hail",
            _ => "—"
        };

        public static string Symbol(int code, bool day) => code switch
        {
            0 => day ? "☀" : "🌙",
            1 => day ? "🌤" : "🌙",
            2 => "⛅",
            3 => "☁",
            45 or 48 => "🌫",
            51 or 53 or 55 or 56 or 57 => "🌦",
            61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧",
            71 or 73 or 75 or 77 or 85 or 86 => "🌨",
            95 or 96 or 99 => "⛈",
            _ => "☁"
        };
    }
}
