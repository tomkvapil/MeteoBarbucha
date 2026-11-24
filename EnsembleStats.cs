using System;
using System.Collections.Generic;

namespace MeteoSrazky
{
    public static class EnsembleStats
    {
        public const string KeyMean = "prumer_modelu";
        public const string KeyMedian = "median_modelu";
        public const string KeyP10 = "p10_modelu";
        public const string KeyP90 = "p90_modelu";
        public const string KeyMax = "max_modelu";
        public const string KeySpread = "rozptyl_modelu";
        public const string KeyAgreement = "shoda_modelu";

        public static bool IsSummaryMapKey(string key)
        {
            return key == KeyMean ||
                   key == KeyMedian ||
                   key == KeyP10 ||
                   key == KeyP90 ||
                   key == KeyMax ||
                   key == KeySpread ||
                   key == KeyAgreement;
        }

        public static bool IsBelowThresholdWarning(WeatherParameter param)
        {
            return param == WeatherParameter.Temperature2mMin;
        }

        public static bool ExceedsThreshold(double val, double threshold, WeatherParameter param)
        {
            return IsBelowThresholdWarning(param) ? (val <= threshold) : (val >= threshold);
        }

        public static double? ComputePercentile(List<double> values, double percentile)
        {
            if (values == null || values.Count == 0)
                return null;

            var sorted = new List<double>(values);
            sorted.Sort();
            if (sorted.Count == 1)
                return Math.Round(sorted[0], 2);

            double pos = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(pos);
            int upper = (int)Math.Ceiling(pos);
            if (lower == upper)
                return Math.Round(sorted[lower], 2);

            double weight = pos - lower;
            double interp = sorted[lower] * (1.0 - weight) + sorted[upper] * weight;
            return Math.Round(interp, 2);
        }

        public static double? ComputeSummaryFromValues(
            List<double> validValues,
            string summaryKey,
            double effectiveThreshold,
            WeatherParameter param,
            out int agreeHits,
            out int totalCount)
        {
            agreeHits = 0;
            totalCount = validValues != null ? validValues.Count : 0;
            if (validValues == null || validValues.Count == 0)
                return null;

            double sum = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            for (int i = 0; i < validValues.Count; i++)
            {
                double v = validValues[i];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
                if (ExceedsThreshold(v, effectiveThreshold, param))
                    agreeHits++;
            }

            if (summaryKey == KeyMean)
                return Math.Round(sum / validValues.Count, 2);
            if (summaryKey == KeyMedian)
                return ComputePercentile(validValues, 50.0);
            if (summaryKey == KeyP10)
                return ComputePercentile(validValues, 10.0);
            if (summaryKey == KeyP90)
                return ComputePercentile(validValues, 90.0);
            if (summaryKey == KeyMax)
                return Math.Round(IsBelowThresholdWarning(param) ? min : max, 2);
            if (summaryKey == KeySpread)
                return Math.Round(max - min, 2);
            if (summaryKey == KeyAgreement)
                return Math.Round((agreeHits * 100.0) / validValues.Count, 1);

            return null;
        }

        public static bool IsModelRunStale(ForecastResult result, string modelId)
        {
            if (result == null || result.ModelRunUtcTimes == null || string.IsNullOrEmpty(modelId))
                return false;

            DateTime utcRun;
            if (!result.ModelRunUtcTimes.TryGetValue(modelId, out utcRun))
                return false;

            return (DateTime.UtcNow - utcRun).TotalHours > 12.0;
        }

        public static double? GetModelRunAgeHours(ForecastResult result, string modelId)
        {
            if (result == null || result.ModelRunUtcTimes == null || string.IsNullOrEmpty(modelId))
                return null;

            DateTime utcRun;
            if (!result.ModelRunUtcTimes.TryGetValue(modelId, out utcRun))
                return null;

            return Math.Max(0.0, (DateTime.UtcNow - utcRun).TotalHours);
        }

        public static void SnapshotBaseValues(ForecastResult result)
        {
            if (result == null) return;

            result.BaseRegionModelSums.Clear();
            foreach (var kv in result.RegionModelSums)
            {
                var copy = new Dictionary<string, double?>();
                foreach (var mkv in kv.Value)
                    copy[mkv.Key] = mkv.Value;
                result.BaseRegionModelSums[kv.Key] = copy;
            }

            if (result.GridPoints != null)
            {
                foreach (var gp in result.GridPoints)
                {
                    gp.BaseModelValues.Clear();
                    foreach (var mkv in gp.ModelValues)
                        gp.BaseModelValues[mkv.Key] = mkv.Value;
                }
            }
        }

        public static void ApplyRollingWindow(ForecastResult result, int windowHours)
        {
            if (result == null) return;
            result.RollingWindowHours = windowHours;

            if (windowHours <= 0)
            {
                // Restore full-period base values
                foreach (var kv in result.BaseRegionModelSums)
                {
                    if (!result.RegionModelSums.ContainsKey(kv.Key))
                        result.RegionModelSums[kv.Key] = new Dictionary<string, double?>();
                    foreach (var mkv in kv.Value)
                        result.RegionModelSums[kv.Key][mkv.Key] = mkv.Value;
                }

                if (result.GridPoints != null)
                {
                    foreach (var gp in result.GridPoints)
                    {
                        foreach (var mkv in gp.BaseModelValues)
                            gp.ModelValues[mkv.Key] = mkv.Value;
                    }
                }
                return;
            }

            var paramInfo = CzechData.GetParameterInfo(result.Parameter);
            if (result.GridPoints != null)
            {
                foreach (var gp in result.GridPoints)
                {
                    foreach (var m in result.Models)
                    {
                        double?[] series;
                        if (gp.ModelSeries != null && gp.ModelSeries.TryGetValue(m.Id, out series) && series != null && series.Length > 0)
                        {
                            gp.ModelValues[m.Id] = ComputeMaxSlidingWindow(series, windowHours, paramInfo.Aggregation);
                        }
                    }
                }
            }

            // Recompute regional centroid approximations from nearest grid point (or keep null if unavailable)
            foreach (var r in CzechData.Regions)
            {
                GeoGridPoint nearest = FindNearestGridPoint(result.GridPoints, r.Latitude, r.Longitude);
                foreach (var m in result.Models)
                {
                    if (nearest != null && nearest.ModelValues.ContainsKey(m.Id) && nearest.ModelValues[m.Id].HasValue)
                    {
                        result.RegionModelSums[r.Code][m.Id] = nearest.ModelValues[m.Id];
                    }
                    else if (result.BaseRegionModelSums.ContainsKey(r.Code) && result.BaseRegionModelSums[r.Code].ContainsKey(m.Id))
                    {
                        // For regional-only models without hourly series (e.g. Meteologix), leave null in short window mode to avoid false comparison
                        result.RegionModelSums[r.Code][m.Id] = null;
                    }
                }
            }
        }

        private static GeoGridPoint FindNearestGridPoint(List<GeoGridPoint> pts, double lat, double lon)
        {
            if (pts == null || pts.Count == 0) return null;
            GeoGridPoint best = null;
            double bestD2 = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                double dLat = pts[i].Latitude - lat;
                double dLon = (pts[i].Longitude - lon) * 0.65;
                double d2 = dLat * dLat + dLon * dLon;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = pts[i];
                }
            }
            return best;
        }

        public static double? ComputeMaxSlidingWindow(double?[] series, int windowHours, TimeAggregation agg)
        {
            if (series == null || series.Length == 0)
                return null;

            int w = Math.Max(1, Math.Min(windowHours, series.Length));
            double best = agg == TimeAggregation.Min ? double.MaxValue : double.MinValue;
            bool anyValid = false;

            for (int start = 0; start <= series.Length - w; start++)
            {
                double sum = 0;
                double wMax = double.MinValue;
                double wMin = double.MaxValue;
                int cnt = 0;

                for (int k = 0; k < w; k++)
                {
                    double? v = series[start + k];
                    if (v.HasValue)
                    {
                        sum += v.Value;
                        if (v.Value > wMax) wMax = v.Value;
                        if (v.Value < wMin) wMin = v.Value;
                        cnt++;
                    }
                }

                if (cnt == 0) continue;

                double windowVal;
                switch (agg)
                {
                    case TimeAggregation.Sum:
                        // Scale proportionally only if at least half of the window slots are valid
                        if (cnt < Math.Max(1, w / 2)) continue;
                        windowVal = sum;
                        break;
                    case TimeAggregation.Max:
                        windowVal = wMax;
                        break;
                    case TimeAggregation.Min:
                        windowVal = wMin;
                        break;
                    default:
                        windowVal = sum / cnt;
                        break;
                }

                if (!anyValid)
                {
                    best = windowVal;
                    anyValid = true;
                }
                else if (agg == TimeAggregation.Min)
                {
                    if (windowVal < best) best = windowVal;
                }
                else
                {
                    if (windowVal > best) best = windowVal;
                }
            }

            return anyValid ? (double?)Math.Round(best, 2) : null;
        }
    }
}
