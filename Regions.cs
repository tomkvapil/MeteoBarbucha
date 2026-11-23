using System;
using System.Collections.Generic;
using System.Drawing;

namespace MeteoSrazky
{
    public static class CzechData
    {
        public static readonly List<CzechRegion> Regions = new List<CzechRegion>
        {
            new CzechRegion { Code = "PHA", Name = "Hlavní město Praha", CapitalCity = "Praha", Latitude = 50.0755, Longitude = 14.4378 },
            new CzechRegion { Code = "STC", Name = "Středočeský kraj", CapitalCity = "", Latitude = 49.9500, Longitude = 14.5500 },
            new CzechRegion { Code = "JHC", Name = "Jihočeský kraj", CapitalCity = "Č. Budějovice", Latitude = 48.9745, Longitude = 14.4743 },
            new CzechRegion { Code = "PLK", Name = "Plzeňský kraj", CapitalCity = "Plzeň", Latitude = 49.7384, Longitude = 13.3736 },
            new CzechRegion { Code = "KVK", Name = "Karlovarský kraj", CapitalCity = "Karlovy Vary", Latitude = 50.2327, Longitude = 12.8712 },
            new CzechRegion { Code = "ULK", Name = "Ústecký kraj", CapitalCity = "Ústí n. L.", Latitude = 50.6607, Longitude = 14.0323 },
            new CzechRegion { Code = "LBK", Name = "Liberecký kraj", CapitalCity = "Liberec", Latitude = 50.7663, Longitude = 15.0543 },
            new CzechRegion { Code = "HKK", Name = "Královéhradecký kraj", CapitalCity = "Hradec Králové", Latitude = 50.2104, Longitude = 15.8252 },
            new CzechRegion { Code = "PAK", Name = "Pardubický kraj", CapitalCity = "Pardubice", Latitude = 50.0343, Longitude = 15.7812 },
            new CzechRegion { Code = "VYS", Name = "Kraj Vysočina", CapitalCity = "Jihlava", Latitude = 49.3961, Longitude = 15.5912 },
            new CzechRegion { Code = "JHM", Name = "Jihomoravský kraj", CapitalCity = "Brno", Latitude = 49.1951, Longitude = 16.6068 },
            new CzechRegion { Code = "OLK", Name = "Olomoucký kraj", CapitalCity = "Olomouc", Latitude = 49.5938, Longitude = 17.2509 },
            new CzechRegion { Code = "ZLK", Name = "Zlínský kraj", CapitalCity = "Zlín", Latitude = 49.2244, Longitude = 17.6628 },
            new CzechRegion { Code = "MSK", Name = "Moravskoslezský kraj", CapitalCity = "Ostrava", Latitude = 49.8209, Longitude = 18.2625 }
        };

        private static string[] _polygonRegionCodes;

        public static string FindRegionCode(double lat, double lon)
        {
            EnsurePolygonRegionMapping();
            for (int i = 0; i < CzechGeoData.KrajePolygons.Length; i++)
            {
                if (PointInRing(lon, lat, CzechGeoData.KrajePolygons[i]))
                    return _polygonRegionCodes[i];
            }
            return null;
        }

        private static void EnsurePolygonRegionMapping()
        {
            if (_polygonRegionCodes != null) return;
            _polygonRegionCodes = new string[CzechGeoData.KrajePolygons.Length];
            for (int i = 0; i < CzechGeoData.KrajePolygons.Length; i++)
            {
                foreach (var r in Regions)
                {
                    if (PointInRing(r.Longitude, r.Latitude, CzechGeoData.KrajePolygons[i]))
                    {
                        _polygonRegionCodes[i] = r.Code;
                        if (r.Code == "PHA") break; // Prefer PHA over STC if inside Prague
                    }
                }
            }
        }

        private static bool PointInRing(double x, double y, float[] ring)
        {
            bool inside = false;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = ring[i * 2], yi = ring[i * 2 + 1];
                double xj = ring[j * 2], yj = ring[j * 2 + 1];
                if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi))
                    inside = !inside;
            }
            return inside;
        }

        public static readonly List<WeatherParameterInfo> AvailableParameters = new List<WeatherParameterInfo>
        {
            new WeatherParameterInfo { Parameter = WeatherParameter.TotalPrecipitation, DisplayName = "Celkové srážky (mm)", MapTitle = "Sumy srážek ČR", LegendTitle = "Srážky (mm):", Unit = "mm", DailyVar = "precipitation_sum", HourlyVar = "precipitation", Aggregation = TimeAggregation.Sum, FilePrefix = "srazky" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Snowfall, DisplayName = "Sníh / Sněžení (cm)", MapTitle = "Sněžení ČR", LegendTitle = "Sněžení (cm):", Unit = "cm", DailyVar = "snowfall_sum", HourlyVar = "snowfall", Aggregation = TimeAggregation.Sum, FilePrefix = "snih" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Temperature2mMax, DisplayName = "Teplota 2m — Maximum (°C)", MapTitle = "Max. teplota 2m ČR", LegendTitle = "Teplota (°C):", Unit = "°C", DailyVar = "temperature_2m_max", HourlyVar = "temperature_2m", Aggregation = TimeAggregation.Max, FilePrefix = "tmax" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Temperature2mMin, DisplayName = "Teplota 2m — Minimum (°C)", MapTitle = "Min. teplota 2m ČR", LegendTitle = "Teplota (°C):", Unit = "°C", DailyVar = "temperature_2m_min", HourlyVar = "temperature_2m", Aggregation = TimeAggregation.Min, FilePrefix = "tmin" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Temperature850hPa, DisplayName = "Teplota v 850 hPa (°C)", MapTitle = "Teplota v 850 hPa ČR", LegendTitle = "T850 (°C):", Unit = "°C", DailyVar = "temperature_850hPa", HourlyVar = "temperature_850hPa", ForceHourly = true, Aggregation = TimeAggregation.Mean, FilePrefix = "t850" },
            new WeatherParameterInfo { Parameter = WeatherParameter.CloudCover, DisplayName = "Oblačnost (%)", MapTitle = "Průměrná oblačnost ČR", LegendTitle = "Oblačnost (%):", Unit = "%", DailyVar = "cloud_cover_mean", HourlyVar = "cloud_cover", Aggregation = TimeAggregation.Mean, FilePrefix = "oblacnost" },
            new WeatherParameterInfo { Parameter = WeatherParameter.WindGusts10m, DisplayName = "Nárazy větru 10m (km/h)", MapTitle = "Max. nárazy větru ČR", LegendTitle = "Nárazy (km/h):", Unit = "km/h", DailyVar = "wind_gusts_10m_max", HourlyVar = "wind_gusts_10m", Aggregation = TimeAggregation.Max, FilePrefix = "narazy_vetru" },
            new WeatherParameterInfo { Parameter = WeatherParameter.WindSpeed10m, DisplayName = "Rychlost větru 10m (km/h)", MapTitle = "Max. rychlost větru ČR", LegendTitle = "Vítr (km/h):", Unit = "km/h", DailyVar = "wind_speed_10m_max", HourlyVar = "wind_speed_10m", Aggregation = TimeAggregation.Max, FilePrefix = "vitr" },
            new WeatherParameterInfo { Parameter = WeatherParameter.UtciMax, DisplayName = "UTCI — Max. tepelná zátěž (°C)", MapTitle = "Max. UTCI (tepelná zátěž) ČR", LegendTitle = "UTCI (°C):", Unit = "°C", DailyVar = "temperature_2m,relative_humidity_2m,wind_speed_10m,shortwave_radiation", HourlyVar = "temperature_2m,relative_humidity_2m,wind_speed_10m,shortwave_radiation", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "utci" },
            new WeatherParameterInfo { Parameter = WeatherParameter.SignificantWeather, DisplayName = "Significant Weather — Významné počasí (WMO)", MapTitle = "Significant Weather ČR", LegendTitle = "Počasí:", Unit = "WMO", DailyVar = "weather_code", HourlyVar = "weather_code", Aggregation = TimeAggregation.Max, FilePrefix = "sigwx" }
        };

        public static string GetWmoShortLabel(double code)
        {
            int c = (int)Math.Round(code);
            if (c <= 0) return "Jasno";
            if (c <= 2) return "Polojasno";
            if (c == 3) return "Zataženo";
            if (c <= 48) return "Mlha";
            if (c <= 57) return "Mrholení";
            if (c <= 65) return "Déšť";
            if (c <= 67) return "Mrzn. déšť";
            if (c <= 77) return "Sněžení";
            if (c <= 82) return "Přeháňky";
            if (c <= 86) return "Sněh. přeh.";
            return "Bouřka";
        }

        public static WeatherParameterInfo GetParameterInfo(WeatherParameter param)
        {
            foreach (var p in AvailableParameters)
                if (p.Parameter == param) return p;
            return AvailableParameters[0];
        }

        public static readonly List<WeatherModelInfo> AvailableModels = new List<WeatherModelInfo>
        {
            new WeatherModelInfo { Id = "chmi_aladin_cz_1km", DisplayName = "ALADIN CZ (1 km, ČHMÚ)", Provider = "ČHMÚ" },
            new WeatherModelInfo { Id = "ecmwf_ifs025", DisplayName = "ECMWF IFS 0.25°", Provider = "ECMWF" },
            new WeatherModelInfo { Id = "icon_eu", DisplayName = "ICON-EU (Evropa)", Provider = "DWD" },
            new WeatherModelInfo { Id = "icon_d2", DisplayName = "ICON-D2 (2.2 km)", Provider = "DWD" },
            new WeatherModelInfo { Id = "dwd_icon_d2_ruc_native", DisplayName = "ICON-D2 RUC (DWD)", Provider = "DWD" },
            new WeatherModelInfo { Id = "gfs_global", DisplayName = "GFS Global", Provider = "NOAA" },
            new WeatherModelInfo { Id = "gem_global", DisplayName = "GEM Global", Provider = "CMC" },
            new WeatherModelInfo { Id = "jma_seamless", DisplayName = "JMA Seamless", Provider = "JMA" },
            new WeatherModelInfo { Id = "ukmo_seamless", DisplayName = "UKMO Seamless", Provider = "Met Office" },
            new WeatherModelInfo { Id = "meteofrance_arpege_europe", DisplayName = "ARPEGE Europe", Provider = "Météo-France" }
        };

        // Simplified geographic outline of Czechia (Lon, Lat) for map snapshot rendering
        public static readonly PointF[] CzechBorderLatLon = new PointF[]
        {
            new PointF(12.09f, 50.32f), // Aš west
            new PointF(12.24f, 50.37f),
            new PointF(12.90f, 50.45f), // Krušné hory
            new PointF(13.60f, 50.72f),
            new PointF(14.23f, 50.89f), // Děčín / Šluknov
            new PointF(14.43f, 51.05f), // Severní výběžek
            new PointF(14.85f, 50.87f),
            new PointF(15.02f, 51.02f), // Frýdlant
            new PointF(15.40f, 50.82f), // Krkonoše
            new PointF(16.05f, 50.65f),
            new PointF(16.30f, 50.55f), // Broumov
            new PointF(16.65f, 50.15f), // Orlické hory
            new PointF(17.00f, 50.35f), // Jeseníky / Javorník
            new PointF(17.65f, 50.30f),
            new PointF(18.25f, 49.95f), // Ostrava
            new PointF(18.85f, 49.55f), // Jablunkov east
            new PointF(18.40f, 49.35f),
            new PointF(18.05f, 49.05f), // Bílé Karpaty
            new PointF(17.50f, 48.80f),
            new PointF(16.95f, 48.62f), // Břeclav south
            new PointF(16.45f, 48.75f),
            new PointF(15.90f, 48.85f), // Znojmo
            new PointF(15.25f, 48.95f),
            new PointF(14.70f, 48.65f), // Novohradské hory
            new PointF(14.25f, 48.55f), // Vyšší Brod south
            new PointF(13.75f, 48.80f), // Šumava
            new PointF(13.25f, 49.10f),
            new PointF(12.75f, 49.45f), // Český les
            new PointF(12.40f, 49.85f),
            new PointF(12.09f, 50.32f)
        };
    }
}
