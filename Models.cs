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
        CloudCover,
        WindGusts10m,
        WindSpeed10m
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

    public enum RegionAggregationMode
    {
        RegionAverage,
        RegionMax,
        PointValue
    }

    public class GeoGridPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string RegionCode { get; set; }
        public Dictionary<string, double?> ModelValues { get; set; }

        public GeoGridPoint()
        {
            ModelValues = new Dictionary<string, double?>();
        }
    }

    public class ForecastResult
    {
        public DateTime FetchedAt { get; set; }
        public int ForecastDays { get; set; }
        public WeatherParameter Parameter { get; set; }
        public string Unit { get; set; }
        public string PeriodDescription { get; set; }
        public List<string> Dates { get; set; }
        public List<WeatherModelInfo> Models { get; set; }
        // Key: Region.Code -> (Key: Model.Id -> Sum in mm/cm, or null if unavailable)
        public Dictionary<string, Dictionary<string, double?>> RegionModelSums { get; set; }
        public List<GeoGridPoint> GridPoints { get; set; }

        public ForecastResult()
        {
            Unit = "mm";
            Dates = new List<string>();
            Models = new List<WeatherModelInfo>();
            RegionModelSums = new Dictionary<string, Dictionary<string, double?>>();
            GridPoints = new List<GeoGridPoint>();
        }
    }
}
