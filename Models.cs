using System;
using System.Collections.Generic;

namespace MeteoSrazky
{
    public class WeatherModelInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Provider { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class CzechRegion
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string CapitalCity { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public enum WeatherParameter
    {
        TotalPrecipitation,
        Snowfall,
        Temperature2mMax,
        Temperature2mMin,
        Temperature850hPa,
        DewPoint2m,
        CloudCover,
        WindGusts10m,
        WindSpeed10m,
        Cape,
        FreezingLevelHeight,
        UtciMax,
        SignificantWeather
    }

    public enum TimeAggregation
    {
        Sum,
        Max,
        Min,
        Mean
    }

    public class WeatherParameterInfo
    {
        public WeatherParameter Parameter { get; set; }
        public string DisplayName { get; set; }
        public string MapTitle { get; set; }
        public string LegendTitle { get; set; }
        public string Unit { get; set; }
        public string DailyVar { get; set; }
        public string HourlyVar { get; set; }
        public bool ForceHourly { get; set; }
        public TimeAggregation Aggregation { get; set; }
        public string FilePrefix { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class WarningPreset
    {
        public string Label { get; set; }
        public double Threshold { get; set; }

        public WarningPreset(string label, double threshold)
        {
            Label = label;
            Threshold = threshold;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    public enum RegionAggregationMode
    {
        RegionAverage,
        RegionMax,
        PointValue
    }

    public enum DataSourcePreference
    {
        AutoMultiSource,
        AlternativeOpenOnly,
        PreferMeteologix,
        OpenMeteoOnly,
        MeteologixOnly
    }

    public class GeoGridPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string RegionCode { get; set; }
        public Dictionary<string, double?> ModelValues { get; set; }
        public Dictionary<string, double?> BaseModelValues { get; set; }
        public Dictionary<string, double?[]> ModelSeries { get; set; }

        public GeoGridPoint()
        {
            ModelValues = new Dictionary<string, double?>();
            BaseModelValues = new Dictionary<string, double?>();
            ModelSeries = new Dictionary<string, double?[]>();
        }
    }

    public class ForecastResult
    {
        public DateTime FetchedAt { get; set; }
        public int ForecastDays { get; set; }
        public int RollingWindowHours { get; set; }
        public WeatherParameter Parameter { get; set; }
        public string Unit { get; set; }
        public string DataSourceLabel { get; set; }
        public bool IsOfflineFallback { get; set; }
        public DateTime OfflineCachedAt { get; set; }
        public double? WarningThreshold { get; set; }
        public double EffectiveAgreementThreshold { get; set; }
        public string PeriodDescription { get; set; }
        public List<string> Dates { get; set; }
        public List<string> TimeSteps { get; set; }
        public int SelectedTimeStepIndex { get; set; }
        public List<WeatherModelInfo> Models { get; set; }
        public Dictionary<string, Dictionary<string, double?>> RegionModelSums { get; set; }
        public Dictionary<string, Dictionary<string, double?>> BaseRegionModelSums { get; set; }
        public List<GeoGridPoint> GridPoints { get; set; }
        public Dictionary<string, string> ModelRunTimes { get; set; }
        public Dictionary<string, DateTime> ModelRunUtcTimes { get; set; }
        public Dictionary<string, string> ModelNotes { get; set; }

        public ForecastResult()
        {
            Unit = "mm";
            SelectedTimeStepIndex = -1;
            EffectiveAgreementThreshold = 30.0;
            Dates = new List<string>();
            TimeSteps = new List<string>();
            Models = new List<WeatherModelInfo>();
            RegionModelSums = new Dictionary<string, Dictionary<string, double?>>();
            BaseRegionModelSums = new Dictionary<string, Dictionary<string, double?>>();
            GridPoints = new List<GeoGridPoint>();
            ModelRunTimes = new Dictionary<string, string>();
            ModelRunUtcTimes = new Dictionary<string, DateTime>();
            ModelNotes = new Dictionary<string, string>();
        }
    }
}
