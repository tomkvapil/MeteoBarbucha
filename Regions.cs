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

        public static bool IsNearOrInsideCzechia(double lat, double lon, double bufferDeg)
        {
            if (FindRegionCode(lat, lon) != null)
                return true;

            double maxDist2 = bufferDeg * bufferDeg;
            for (int i = 0; i < CzechBorderLatLon.Length; i++)
            {
                double dLat = lat - CzechBorderLatLon[i].Y;
                double dLon = (lon - CzechBorderLatLon[i].X) * 0.65;
                if (dLat * dLat + dLon * dLon <= maxDist2)
                    return true;
            }

            for (int r = 0; r < CzechGeoData.KrajePolygons.Length; r++)
            {
                float[] ring = CzechGeoData.KrajePolygons[r];
                int n = ring.Length / 2;
                for (int k = 0; k < n; k += 3)
                {
                    double dLat = lat - ring[k * 2 + 1];
                    double dLon = (lon - ring[k * 2]) * 0.65;
                    if (dLat * dLat + dLon * dLon <= maxDist2)
                        return true;
                }
            }
            return false;
        }

        public static bool IsOpenMeteoModel(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return false;
            if (modelId == AlternativeWeatherProviders.MetNoId || modelId == AlternativeWeatherProviders.BrightSkyId)
                return false;
            if (modelId.StartsWith("meteologix_", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        public static bool IsHighResModel(string modelId)
        {
            return modelId == "chmi_aladin_cz_1km" ||
                   modelId == "icon_d2" ||
                   modelId == "dwd_icon_d2_ruc_native" ||
                   modelId == "icon_eu" ||
                   modelId == "meteofrance_arpege_europe";
        }

        public static bool IsGlobalModel(string modelId)
        {
            return modelId == "ecmwf_ifs025" ||
                   modelId == "gfs_global" ||
                   modelId == "gem_global" ||
                   modelId == "ukmo_seamless" ||
                   modelId == "jma_seamless";
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
                        if (r.Code == "PHA") break;
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
            new WeatherParameterInfo { Parameter = WeatherParameter.TotalPrecipitation, DisplayName = "Celkové srážky (mm)", MapTitle = "Sumy srážek ČR", LegendTitle = "Srážky (mm):", Unit = "mm", DailyVar = "precipitation_sum", HourlyVar = "precipitation", ForceHourly = true, Aggregation = TimeAggregation.Sum, FilePrefix = "srazky" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Snowfall, DisplayName = "Sníh / Sněžení (cm)", MapTitle = "Sněžení ČR", LegendTitle = "Sněžení (cm):", Unit = "cm", DailyVar = "snowfall_sum", HourlyVar = "snowfall", ForceHourly = true, Aggregation = TimeAggregation.Sum, FilePrefix = "snih" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Temperature2mMax, DisplayName = "Teplota 2m — Maximum (°C)", MapTitle = "Max. teplota 2m ČR", LegendTitle = "Teplota (°C):", Unit = "°C", DailyVar = "temperature_2m_max", HourlyVar = "temperature_2m", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "tmax" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Temperature2mMin, DisplayName = "Teplota 2m — Minimum (°C)", MapTitle = "Min. teplota 2m ČR", LegendTitle = "Teplota (°C):", Unit = "°C", DailyVar = "temperature_2m_min", HourlyVar = "temperature_2m", ForceHourly = true, Aggregation = TimeAggregation.Min, FilePrefix = "tmin" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Temperature850hPa, DisplayName = "Teplota v 850 hPa (°C)", MapTitle = "Teplota v 850 hPa ČR", LegendTitle = "T850 (°C):", Unit = "°C", DailyVar = "temperature_850hPa", HourlyVar = "temperature_850hPa", ForceHourly = true, Aggregation = TimeAggregation.Mean, FilePrefix = "t850" },
            new WeatherParameterInfo { Parameter = WeatherParameter.DewPoint2m, DisplayName = "Rosný bod 2m — Maximum (°C)", MapTitle = "Max. rosný bod 2m ČR", LegendTitle = "Rosný bod (°C):", Unit = "°C", DailyVar = "dew_point_2m", HourlyVar = "dew_point_2m", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "td2m" },
            new WeatherParameterInfo { Parameter = WeatherParameter.CloudCover, DisplayName = "Oblačnost (%)", MapTitle = "Průměrná oblačnost ČR", LegendTitle = "Oblačnost (%):", Unit = "%", DailyVar = "cloud_cover_mean", HourlyVar = "cloud_cover", ForceHourly = true, Aggregation = TimeAggregation.Mean, FilePrefix = "oblacnost" },
            new WeatherParameterInfo { Parameter = WeatherParameter.WindGusts10m, DisplayName = "Nárazy větru 10m (km/h)", MapTitle = "Max. nárazy větru ČR", LegendTitle = "Nárazy (km/h):", Unit = "km/h", DailyVar = "wind_gusts_10m_max", HourlyVar = "wind_gusts_10m", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "narazy_vetru" },
            new WeatherParameterInfo { Parameter = WeatherParameter.WindSpeed10m, DisplayName = "Rychlost větru 10m (km/h)", MapTitle = "Max. rychlost větru ČR", LegendTitle = "Vítr (km/h):", Unit = "km/h", DailyVar = "wind_speed_10m_max", HourlyVar = "wind_speed_10m", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "vitr" },
            new WeatherParameterInfo { Parameter = WeatherParameter.Cape, DisplayName = "CAPE — Konvekční energie (J/kg)", MapTitle = "Max. CAPE ČR", LegendTitle = "CAPE (J/kg):", Unit = "J/kg", DailyVar = "cape", HourlyVar = "cape", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "cape" },
            new WeatherParameterInfo { Parameter = WeatherParameter.FreezingLevelHeight, DisplayName = "Výška nulové izotermy (m n. m.)", MapTitle = "Výška nulové izotermy (0 °C) ČR", LegendTitle = "Hladina 0 °C (m):", Unit = "m", DailyVar = "freezing_level_height", HourlyVar = "freezing_level_height", ForceHourly = true, Aggregation = TimeAggregation.Mean, FilePrefix = "nulova_izoterma" },
            new WeatherParameterInfo { Parameter = WeatherParameter.UtciMax, DisplayName = "UTCI — Max. tepelná zátěž (°C)", MapTitle = "Max. UTCI (tepelná zátěž) ČR", LegendTitle = "UTCI (°C):", Unit = "°C", DailyVar = "temperature_2m,relative_humidity_2m,wind_speed_10m,shortwave_radiation", HourlyVar = "temperature_2m,relative_humidity_2m,wind_speed_10m,shortwave_radiation", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "utci" },
            new WeatherParameterInfo { Parameter = WeatherParameter.SignificantWeather, DisplayName = "Significant Weather — Významné počasí (WMO)", MapTitle = "Significant Weather ČR", LegendTitle = "Počasí:", Unit = "WMO", DailyVar = "weather_code", HourlyVar = "weather_code", ForceHourly = true, Aggregation = TimeAggregation.Max, FilePrefix = "sigwx" }
        };

        public static List<WarningPreset> GetWarningPresets(WeatherParameter param)
        {
            switch (param)
            {
                case WeatherParameter.TotalPrecipitation:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("ČHMÚ 1. st. — Přívalový déšť (15 mm / 1 h)", 15.0),
                        new WarningPreset("ČHMÚ 1. st. — Silný déšť (30 mm / 6–24 h)", 30.0),
                        new WarningPreset("ČHMÚ 2. st. — Velmi silný déšť (50 mm)", 50.0),
                        new WarningPreset("ČHMÚ 3. st. — Extrémní srážky (80 mm)", 80.0)
                    };
                case WeatherParameter.Snowfall:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("ČHMÚ 1. st. — Nová sněhová pokrývka (7 cm / 12 h)", 7.0),
                        new WarningPreset("ČHMÚ 2. st. — Vysoká sněhová pokrývka (15 cm / 12 h)", 15.0),
                        new WarningPreset("ČHMÚ 3. st. — Extrémní sněžení (30 cm / 24 h)", 30.0)
                    };
                case WeatherParameter.WindGusts10m:
                case WeatherParameter.WindSpeed10m:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("ČHMÚ 1. st. — Silný vítr (65 km/h)", 65.0),
                        new WarningPreset("ČHMÚ 2. st. — Velmi silný vítr (90 km/h)", 90.0),
                        new WarningPreset("ČHMÚ 3. st. — Extrémní vítr / orkán (110 km/h)", 110.0)
                    };
                case WeatherParameter.Temperature2mMax:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("ČHMÚ 1. st. — Vysoké teploty (31 °C)", 31.0),
                        new WarningPreset("ČHMÚ 2. st. — Velmi vysoké teploty (34 °C)", 34.0),
                        new WarningPreset("Extrémní vedro (37 °C)", 37.0)
                    };
                case WeatherParameter.Temperature2mMin:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("Mráz pod nulou (0 °C)", 0.0),
                        new WarningPreset("ČHMÚ 1. st. — Silný mráz (−12 °C)", -12.0),
                        new WarningPreset("ČHMÚ 2. st. — Velmi silný mráz (−18 °C)", -18.0),
                        new WarningPreset("ČHMÚ 3. st. — Extrémní mráz (−24 °C)", -24.0)
                    };
                case WeatherParameter.Cape:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("Mírná instabilita (500 J/kg)", 500.0),
                        new WarningPreset("Silné bouřky (1200 J/kg)", 1200.0),
                        new WarningPreset("Velmi silné / supercelární bouřky (2000 J/kg)", 2000.0)
                    };
                case WeatherParameter.UtciMax:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("Mírná tepelná zátěž (26 °C)", 26.0),
                        new WarningPreset("Silná tepelná zátěž (32 °C)", 32.0),
                        new WarningPreset("Velmi silná tepelná zátěž (38 °C)", 38.0),
                        new WarningPreset("Extrémní tepelná zátěž (46 °C)", 46.0)
                    };
                case WeatherParameter.DewPoint2m:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("Dusno — práh ČHMÚ (16 °C)", 16.0),
                        new WarningPreset("Silné dusno / vlhkost pro konvekci (19 °C)", 19.0)
                    };
                default:
                    return new List<WarningPreset>
                    {
                        new WarningPreset("Výchozí práh (30)", 30.0)
                    };
            }
        }

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
            new WeatherModelInfo { Id = "meteofrance_arpege_europe", DisplayName = "ARPEGE Europe", Provider = "Météo-France" },
            new WeatherModelInfo { Id = "metno_seamless", DisplayName = "MET Norway (YR.no / ECMWF)", Provider = "MET Norway" },
            new WeatherModelInfo { Id = "brightsky_dwd", DisplayName = "Bright Sky (DWD Open Data)", Provider = "DWD / BrightSky" }
        };

        public static readonly PointF[] CzechBorderLatLon = new PointF[]
        {
            new PointF(12.09f, 50.32f),
            new PointF(12.24f, 50.37f),
            new PointF(12.90f, 50.45f),
            new PointF(13.60f, 50.72f),
            new PointF(14.23f, 50.89f),
            new PointF(14.43f, 51.05f),
            new PointF(14.85f, 50.87f),
            new PointF(15.02f, 51.02f),
            new PointF(15.40f, 50.82f),
            new PointF(16.05f, 50.65f),
            new PointF(16.30f, 50.55f),
            new PointF(16.65f, 50.15f),
            new PointF(17.00f, 50.35f),
            new PointF(17.65f, 50.30f),
            new PointF(18.25f, 49.95f),
            new PointF(18.85f, 49.55f),
            new PointF(18.40f, 49.35f),
            new PointF(18.05f, 49.05f),
            new PointF(17.50f, 48.80f),
            new PointF(16.95f, 48.62f),
            new PointF(16.45f, 48.75f),
            new PointF(15.90f, 48.85f),
            new PointF(15.25f, 48.95f),
            new PointF(14.70f, 48.65f),
            new PointF(14.25f, 48.55f),
            new PointF(13.75f, 48.80f),
            new PointF(13.25f, 49.10f),
            new PointF(12.75f, 49.45f),
            new PointF(12.40f, 49.85f),
            new PointF(12.09f, 50.32f)
        };
    }
}
