using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MeteoSrazky
{
    public static class AlternativeWeatherProviders
    {
        public const string MetNoId = "metno_seamless";
        public const string BrightSkyId = "brightsky_dwd";

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MeteoBarbuchaCZ/1.4.0 (https://github.com/tomkvapil/MeteoBarbucha; tom.kvapil89@gmail.com)");
            return client;
        }

        private class HourlyRecord
        {
            public DateTime LocalHour;
            public double? TempC;
            public double? RhPct;
            public double? DewPointC;
            public double? WindKmh;
            public double? GustKmh;
            public double? CloudPct;
            public double? PrecipMm;
            public double? SnowCm;
            public double? SolarWm2;
            public double? WmoCode;
        }

        private class SamplePoint
        {
            public double Latitude;
            public double Longitude;
            public string RegionCode;
            public Dictionary<string, List<HourlyRecord>> RecordsByModel = new Dictionary<string, List<HourlyRecord>>();
            public Dictionary<string, double?> AggValueByModel = new Dictionary<string, double?>();
            public Dictionary<string, double?[]> SeriesByModel = new Dictionary<string, double?[]>();
        }

        public static List<WeatherModelInfo> GetDefaultAlternativeModels()
        {
            var list = new List<WeatherModelInfo>();
            foreach (var m in CzechData.AvailableModels)
            {
                if (m.Id == MetNoId || m.Id == BrightSkyId)
                    list.Add(m);
            }
            return list;
        }

        public static string GetDerivedParameterNote(string modelId, WeatherParameter param)
        {
            switch (param)
            {
                case WeatherParameter.Snowfall:
                    return "odvozeno ze srážek a T2m";
                case WeatherParameter.SignificantWeather:
                    return modelId == MetNoId ? "převod ze symbolů YR.no" : "převod ze stavů DWD";
                case WeatherParameter.UtciMax:
                    return modelId == MetNoId ? "záření odhadnuto z oblačnosti" : "vypočteno z T, RH, větru a záření";
                case WeatherParameter.DewPoint2m:
                    return "odvozeno z T2m a RH";
                default:
                    return null;
            }
        }

        public static async Task PopulateAlternativeModelsAsync(
            List<WeatherModelInfo> altModels,
            ForecastResult result,
            DateTime windowStart,
            DateTime windowEnd,
            WeatherParameterInfo paramInfo,
            Action<string> log,
            Action<int, int, string> progress,
            CancellationToken ct)
        {
            if (altModels == null || altModels.Count == 0)
                return;

            bool standaloneGrid = (result.GridPoints == null || result.GridPoints.Count == 0);

            // Populate exact hourly TimeSteps [windowStart .. windowEnd] if standalone
            if (result.TimeSteps.Count == 0)
            {
                DateTime cur = windowStart;
                while (cur <= windowEnd)
                {
                    string ts = cur.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
                    result.Dates.Add(ts);
                    result.TimeSteps.Add(ts);
                    cur = cur.AddHours(1);
                }
            }

            // Build ~58 sample locations: 14 regional centroids + ~44 Czechia & border grid points
            var samples = new List<SamplePoint>();
            foreach (var r in CzechData.Regions)
            {
                samples.Add(new SamplePoint
                {
                    Latitude = Math.Round(r.Latitude, 4),
                    Longitude = Math.Round(r.Longitude, 4),
                    RegionCode = r.Code
                });
            }

            for (double lat = 48.60; lat <= 51.02; lat += 0.50)
            {
                for (double lon = 12.15; lon <= 18.80; lon += 0.70)
                {
                    double rLat = Math.Round(lat, 2);
                    double rLon = Math.Round(lon, 2);
                    if (!CzechData.IsNearOrInsideCzechia(rLat, rLon, 0.28))
                        continue;

                    samples.Add(new SamplePoint
                    {
                        Latitude = rLat,
                        Longitude = rLon,
                        RegionCode = CzechData.FindRegionCode(rLat, rLon)
                    });
                }
            }

            foreach (var model in altModels)
            {
                ct.ThrowIfCancellationRequested();

                // Parameters not available in surface-only alternative endpoints -> null (renders red crossed circle cleanly)
                if (paramInfo.Parameter == WeatherParameter.Temperature850hPa ||
                    paramInfo.Parameter == WeatherParameter.Cape ||
                    paramInfo.Parameter == WeatherParameter.FreezingLevelHeight)
                {
                    EnsureModelAdded(result, model);
                    foreach (var r in CzechData.Regions)
                        result.RegionModelSums[r.Code][model.Id] = null;

                    if (standaloneGrid)
                        EnsureStandaloneGridCreated(result, samples);

                    foreach (var gp in result.GridPoints)
                    {
                        gp.ModelValues[model.Id] = null;
                        gp.ModelSeries[model.Id] = null;
                    }
                    log(model.DisplayName + ": parametr '" + paramInfo.DisplayName + "' není v tomto zdroji k dispozici (vynecháno ze souhrnů).");
                    continue;
                }

                int cacheHits = 0;
                int netCalls = 0;
                int offlineHits = 0;
                int completedPts = 0;
                DateTime oldestOffline = DateTime.MaxValue;
                string runTimeStr = null;
                DateTime? runTimeUtc = null;
                var runLock = new object();
                var sem = new SemaphoreSlim(8);
                var tasks = new List<Task>();

                string bsStartDate = windowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string bsLastDate = windowEnd.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                int totalPts = samples.Count;

                log("Načítám " + model.DisplayName + " (" + totalPts + " bodů ČR)...");
                if (progress != null)
                    progress(0, totalPts, "Stahuji " + model.DisplayName + ": 0 / " + totalPts + " bodů...");

                foreach (var sp in samples)
                {
                    var sample = sp;
                    tasks.Add(Task.Run(async () =>
                    {
                        await sem.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            string url;
                            if (model.Id == MetNoId)
                            {
                                url = string.Format(
                                    CultureInfo.InvariantCulture,
                                    "https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={0:0.0000}&lon={1:0.0000}",
                                    sample.Latitude, sample.Longitude);
                            }
                            else
                            {
                                url = string.Format(
                                    CultureInfo.InvariantCulture,
                                    "https://api.brightsky.dev/weather?lat={0:0.0000}&lon={1:0.0000}&date={2}&last_date={3}",
                                    sample.Latitude, sample.Longitude, bsStartDate, bsLastDate);
                            }

                            string json = DiskCache.TryGet(url);
                            if (!string.IsNullOrEmpty(json))
                            {
                                Interlocked.Increment(ref cacheHits);
                            }
                            else
                            {
                                json = await FetchStringSafeAsync(url, ct).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(json))
                                {
                                    DiskCache.Put(url, json);
                                    Interlocked.Increment(ref netCalls);
                                }
                                else
                                {
                                    DateTime staleAt;
                                    string staleJson = DiskCache.TryGetStale(url, out staleAt);
                                    if (!string.IsNullOrEmpty(staleJson))
                                    {
                                        json = staleJson;
                                        Interlocked.Increment(ref offlineHits);
                                        lock (runLock)
                                        {
                                            if (staleAt < oldestOffline)
                                                oldestOffline = staleAt;
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(json))
                            {
                                string extractedRun;
                                DateTime? extractedUtc;
                                List<HourlyRecord> recs = model.Id == MetNoId
                                    ? ParseMetNoJson(json, out extractedRun, out extractedUtc)
                                    : ParseBrightSkyJson(json, out extractedRun, out extractedUtc);

                                lock (runLock)
                                {
                                    sample.RecordsByModel[model.Id] = recs;
                                    if (runTimeStr == null && !string.IsNullOrEmpty(extractedRun))
                                    {
                                        runTimeStr = extractedRun;
                                        runTimeUtc = extractedUtc;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            int done = Interlocked.Increment(ref completedPts);
                            if (progress != null && (done % 4 == 0 || done == totalPts))
                                progress(done, totalPts, "Stahuji " + model.DisplayName + ": " + done + " / " + totalPts + " bodů...");
                            sem.Release();
                        }
                    }, ct));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);

                int validSamples = 0;
                foreach (var sp in samples)
                {
                    List<HourlyRecord> recs;
                    if (!sp.RecordsByModel.TryGetValue(model.Id, out recs) || recs == null || recs.Count == 0)
                        continue;

                    double? aggVal = EvaluateWindowAggregate(recs, windowStart, windowEnd, paramInfo);
                    double?[] series = EvaluateTimelineSeries(recs, result.TimeSteps, paramInfo);

                    sp.AggValueByModel[model.Id] = aggVal;
                    sp.SeriesByModel[model.Id] = series;
                    if (aggVal.HasValue)
                        validSamples++;
                }

                if (validSamples == 0)
                {
                    log("Upozornění: " + model.DisplayName + " nevrátil žádná platná data.");
                    AppLogger.Warn(model.DisplayName + " nevrátil žádná platná data.");
                    continue;
                }

                EnsureModelAdded(result, model);
                if (!string.IsNullOrEmpty(runTimeStr))
                    result.ModelRunTimes[model.Id] = runTimeStr;
                if (runTimeUtc.HasValue)
                    result.ModelRunUtcTimes[model.Id] = runTimeUtc.Value;

                string derivedNote = GetDerivedParameterNote(model.Id, paramInfo.Parameter);
                if (!string.IsNullOrEmpty(derivedNote))
                    result.ModelNotes[model.Id] = derivedNote;

                if (offlineHits > 0 && netCalls == 0)
                {
                    result.IsOfflineFallback = true;
                    if (oldestOffline < DateTime.MaxValue)
                        result.OfflineCachedAt = oldestOffline;
                    log("⚠ " + model.DisplayName + ": síť nedostupná — načtena OFFLINE záloha z mezipaměti (" + offlineHits + " bodů).");
                }

                // Populate 14 regional centroid values (first 14 samples)
                for (int i = 0; i < CzechData.Regions.Count && i < samples.Count; i++)
                {
                    string rCode = CzechData.Regions[i].Code;
                    double? val;
                    samples[i].AggValueByModel.TryGetValue(model.Id, out val);
                    result.RegionModelSums[rCode][model.Id] = val;
                }

                if (standaloneGrid)
                {
                    EnsureStandaloneGridCreated(result, samples);
                    for (int i = 0; i < samples.Count && i < result.GridPoints.Count; i++)
                    {
                        double? val;
                        samples[i].AggValueByModel.TryGetValue(model.Id, out val);
                        result.GridPoints[i].ModelValues[model.Id] = val;

                        double?[] ser;
                        samples[i].SeriesByModel.TryGetValue(model.Id, out ser);
                        result.GridPoints[i].ModelSeries[model.Id] = ser;
                    }
                }
                else
                {
                    InterpolateOntoGrid(result.GridPoints, samples, model.Id, result.TimeSteps.Count);
                }

                log(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} načteno ({1} z cache, {2} síťových dotazů).",
                    model.DisplayName, cacheHits, netCalls));
            }
        }

        private static void EnsureModelAdded(ForecastResult result, WeatherModelInfo model)
        {
            foreach (var existing in result.Models)
            {
                if (existing.Id == model.Id)
                    return;
            }
            result.Models.Add(model);
        }

        private static void EnsureStandaloneGridCreated(ForecastResult result, List<SamplePoint> samples)
        {
            if (result.GridPoints != null && result.GridPoints.Count > 0)
                return;

            result.GridPoints = new List<GeoGridPoint>(samples.Count);
            foreach (var sp in samples)
            {
                result.GridPoints.Add(new GeoGridPoint
                {
                    Latitude = sp.Latitude,
                    Longitude = sp.Longitude,
                    RegionCode = sp.RegionCode
                });
            }
        }

        private static void InterpolateOntoGrid(List<GeoGridPoint> targetGrid, List<SamplePoint> samples, string modelId, int stepCount)
        {
            foreach (var gp in targetGrid)
            {
                double d0 = double.MaxValue, d1 = double.MaxValue, d2 = double.MaxValue, d3 = double.MaxValue;
                SamplePoint s0 = null, s1 = null, s2 = null, s3 = null;

                foreach (var sp in samples)
                {
                    double? sv;
                    if (!sp.AggValueByModel.TryGetValue(modelId, out sv) || !sv.HasValue)
                        continue;

                    double dLat = gp.Latitude - sp.Latitude;
                    double dLon = (gp.Longitude - sp.Longitude) * 0.643;
                    double dist2 = dLat * dLat + dLon * dLon;

                    if (dist2 < d0)
                    {
                        d3 = d2; s3 = s2;
                        d2 = d1; s2 = s1;
                        d1 = d0; s1 = s0;
                        d0 = dist2; s0 = sp;
                    }
                    else if (dist2 < d1)
                    {
                        d3 = d2; s3 = s2;
                        d2 = d1; s2 = s1;
                        d1 = dist2; s1 = sp;
                    }
                    else if (dist2 < d2)
                    {
                        d3 = d2; s3 = s2;
                        d2 = dist2; s2 = sp;
                    }
                    else if (dist2 < d3)
                    {
                        d3 = dist2; s3 = sp;
                    }
                }

                if (s0 == null)
                {
                    gp.ModelValues[modelId] = null;
                    gp.ModelSeries[modelId] = null;
                    continue;
                }

                if (d0 < 1e-5 || s1 == null)
                {
                    gp.ModelValues[modelId] = s0.AggValueByModel[modelId];
                    double?[] exactSer;
                    s0.SeriesByModel.TryGetValue(modelId, out exactSer);
                    gp.ModelSeries[modelId] = exactSer;
                    continue;
                }

                var nearPts = new SamplePoint[] { s0, s1, s2, s3 };
                var nearD2 = new double[] { d0, d1, d2, d3 };

                double wSum = 0;
                double vSum = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (nearPts[k] == null) continue;
                    double w = 1.0 / Math.Max(1e-5, nearD2[k]);
                    wSum += w;
                    vSum += w * nearPts[k].AggValueByModel[modelId].Value;
                }
                gp.ModelValues[modelId] = Math.Round(vSum / wSum, 2);

                if (stepCount > 0)
                {
                    var outSer = new double?[stepCount];
                    for (int t = 0; t < stepCount; t++)
                    {
                        double sw = 0, sv = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            if (nearPts[k] == null) continue;
                            double?[] ser;
                            if (!nearPts[k].SeriesByModel.TryGetValue(modelId, out ser) || ser == null || t >= ser.Length || !ser[t].HasValue)
                                continue;
                            double w = 1.0 / Math.Max(1e-5, nearD2[k]);
                            sw += w;
                            sv += w * ser[t].Value;
                        }
                        if (sw > 0)
                            outSer[t] = Math.Round(sv / sw, 2);
                    }
                    gp.ModelSeries[modelId] = outSer;
                }
            }
        }

        private static async Task<string> FetchStringSafeAsync(string url, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool shouldRetry = false;
                try
                {
                    using (var resp = await _http.GetAsync(url, ct).ConfigureAwait(false))
                    {
                        if ((int)resp.StatusCode == 429 || (int)resp.StatusCode == 503)
                        {
                            if (attempt == 0)
                            {
                                await Task.Delay(1500, ct).ConfigureAwait(false);
                                continue;
                            }
                            return null;
                        }
                        if (!resp.IsSuccessStatusCode)
                            return null;
                        return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested)
                        throw;
                    shouldRetry = (attempt == 0);
                }
                catch
                {
                    if (attempt == 0)
                        shouldRetry = true;
                }

                if (shouldRetry)
                    await Task.Delay(600, ct).ConfigureAwait(false);
            }
            return null;
        }

        private static double? GetRecordParameterValue(HourlyRecord r, WeatherParameter param)
        {
            switch (param)
            {
                case WeatherParameter.TotalPrecipitation: return r.PrecipMm;
                case WeatherParameter.Snowfall: return r.SnowCm;
                case WeatherParameter.Temperature2mMax:
                case WeatherParameter.Temperature2mMin: return r.TempC;
                case WeatherParameter.DewPoint2m: return r.DewPointC;
                case WeatherParameter.CloudCover: return r.CloudPct;
                case WeatherParameter.WindGusts10m: return r.GustKmh ?? r.WindKmh;
                case WeatherParameter.WindSpeed10m: return r.WindKmh;
                case WeatherParameter.SignificantWeather: return r.WmoCode;
                case WeatherParameter.UtciMax:
                    if (r.TempC.HasValue && r.RhPct.HasValue && r.WindKmh.HasValue)
                        return Math.Round(UtciCalculator.Compute(r.TempC.Value, r.RhPct.Value, r.WindKmh.Value, r.SolarWm2 ?? 0.0), 2);
                    return null;
                default:
                    return null;
            }
        }

        private static double? EvaluateWindowAggregate(List<HourlyRecord> recs, DateTime startLocal, DateTime endLocal, WeatherParameterInfo paramInfo)
        {
            double sum = 0;
            double max = double.MinValue;
            double min = double.MaxValue;
            int cnt = 0;

            foreach (var r in recs)
            {
                if (r.LocalHour < startLocal || r.LocalHour > endLocal)
                    continue;

                double? v = GetRecordParameterValue(r, paramInfo.Parameter);
                if (v.HasValue)
                {
                    sum += v.Value;
                    if (v.Value > max) max = v.Value;
                    if (v.Value < min) min = v.Value;
                    cnt++;
                }
            }

            if (cnt == 0) return null;
            switch (paramInfo.Aggregation)
            {
                case TimeAggregation.Max: return Math.Round(max, 2);
                case TimeAggregation.Min: return Math.Round(min, 2);
                case TimeAggregation.Mean: return Math.Round(sum / cnt, 2);
                default: return Math.Round(sum, 2);
            }
        }

        private static double?[] EvaluateTimelineSeries(List<HourlyRecord> recs, List<string> timeSteps, WeatherParameterInfo paramInfo)
        {
            if (timeSteps == null || timeSteps.Count == 0)
                return null;

            bool isDaily = timeSteps[0].Length <= 10;
            var series = new double?[timeSteps.Count];

            if (isDaily)
            {
                for (int i = 0; i < timeSteps.Count; i++)
                {
                    DateTime dayStart;
                    if (DateTime.TryParseExact(timeSteps[i].Substring(0, Math.Min(10, timeSteps[i].Length)), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dayStart))
                    {
                        series[i] = EvaluateWindowAggregate(recs, dayStart, dayStart.AddHours(23), paramInfo);
                    }
                }
            }
            else
            {
                var byHour = new Dictionary<string, HourlyRecord>();
                foreach (var r in recs)
                {
                    string key = r.LocalHour.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
                    if (!byHour.ContainsKey(key))
                        byHour[key] = r;
                }

                for (int i = 0; i < timeSteps.Count; i++)
                {
                    HourlyRecord hr;
                    if (byHour.TryGetValue(timeSteps[i], out hr))
                        series[i] = GetRecordParameterValue(hr, paramInfo.Parameter);
                }
            }
            return series;
        }

        private static double? ComputeDewPointC(double? tempC, double? rhPct)
        {
            if (!tempC.HasValue || !rhPct.HasValue) return null;
            double rh = Math.Max(1.0, Math.Min(100.0, rhPct.Value));
            const double a = 17.625;
            const double b = 243.04;
            double alpha = Math.Log(rh / 100.0) + (a * tempC.Value) / (b + tempC.Value);
            return Math.Round((b * alpha) / (a - alpha), 2);
        }

        private static List<HourlyRecord> ParseMetNoJson(string json, out string runTimeFormatted, out DateTime? runUtc)
        {
            runTimeFormatted = null;
            runUtc = null;
            var list = new List<HourlyRecord>();
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("properties"))
                return list;

            var props = root["properties"] as Dictionary<string, object>;
            if (props == null)
                return list;

            if (props.ContainsKey("meta"))
            {
                var meta = props["meta"] as Dictionary<string, object>;
                if (meta != null && meta.ContainsKey("updated_at") && meta["updated_at"] != null)
                {
                    DateTime utcUpd;
                    if (DateTime.TryParse(Convert.ToString(meta["updated_at"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utcUpd))
                    {
                        runUtc = utcUpd;
                        runTimeFormatted = string.Format(CultureInfo.InvariantCulture, "{0:dd.MM.yyyy HH:mm} ({1:HH}Z)", utcUpd.ToLocalTime(), utcUpd);
                    }
                }
            }

            if (!props.ContainsKey("timeseries"))
                return list;

            var tsArr = props["timeseries"] as object[];
            if (tsArr == null)
                return list;

            for (int i = 0; i < tsArr.Length; i++)
            {
                var item = tsArr[i] as Dictionary<string, object>;
                if (item == null || !item.ContainsKey("time") || !item.ContainsKey("data"))
                    continue;

                DateTime utcTime;
                if (!DateTime.TryParse(Convert.ToString(item["time"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utcTime))
                    continue;

                DateTime localTime = utcTime.ToLocalTime();
                localTime = new DateTime(localTime.Year, localTime.Month, localTime.Day, localTime.Hour, 0, 0);

                var data = item["data"] as Dictionary<string, object>;
                if (data == null || !data.ContainsKey("instant"))
                    continue;

                var instant = data["instant"] as Dictionary<string, object>;
                var details = (instant != null && instant.ContainsKey("details")) ? instant["details"] as Dictionary<string, object> : null;
                if (details == null)
                    continue;

                double? tempC = ReadDouble(details, "air_temperature");
                double? rhPct = ReadDouble(details, "relative_humidity");
                double? dewC = ReadDouble(details, "dew_point_temperature") ?? ComputeDewPointC(tempC, rhPct);
                double? cloudPct = ReadDouble(details, "cloud_area_fraction");
                double? wsMs = ReadDouble(details, "wind_speed");
                double? wgMs = ReadDouble(details, "wind_speed_of_gust");
                double? windKmh = wsMs.HasValue ? (double?)Math.Round(wsMs.Value * 3.6, 2) : null;
                double? gustKmh = wgMs.HasValue ? (double?)Math.Round(wgMs.Value * 3.6, 2) : null;

                int spanHours = 1;
                double? totalPrecip = null;
                string symbolCode = null;

                if (data.ContainsKey("next_1_hours") && data["next_1_hours"] != null)
                {
                    var n1 = data["next_1_hours"] as Dictionary<string, object>;
                    totalPrecip = ReadNestedDouble(n1, "details", "precipitation_amount");
                    symbolCode = ReadNestedString(n1, "summary", "symbol_code");
                    spanHours = 1;
                }
                else if (data.ContainsKey("next_6_hours") && data["next_6_hours"] != null)
                {
                    var n6 = data["next_6_hours"] as Dictionary<string, object>;
                    totalPrecip = ReadNestedDouble(n6, "details", "precipitation_amount");
                    symbolCode = ReadNestedString(n6, "summary", "symbol_code");
                    spanHours = 6;
                }

                double? wmo = MapMetNoSymbolToWmo(symbolCode, cloudPct);

                for (int h = 0; h < spanHours; h++)
                {
                    DateTime slotHour = localTime.AddHours(h);
                    double? slotPrecip = totalPrecip.HasValue ? (double?)Math.Round(totalPrecip.Value / spanHours, 3) : null;
                    double? slotSnow = ComputeSnowCm(slotPrecip, tempC, symbolCode);
                    double? estSolar = EstimateSolarRadiation(slotHour, cloudPct);

                    list.Add(new HourlyRecord
                    {
                        LocalHour = slotHour,
                        TempC = tempC,
                        RhPct = rhPct,
                        DewPointC = dewC,
                        WindKmh = windKmh,
                        GustKmh = gustKmh,
                        CloudPct = cloudPct,
                        PrecipMm = slotPrecip,
                        SnowCm = slotSnow,
                        SolarWm2 = estSolar,
                        WmoCode = wmo
                    });
                }
            }

            return list;
        }

        private static List<HourlyRecord> ParseBrightSkyJson(string json, out string runTimeFormatted, out DateTime? runUtc)
        {
            runTimeFormatted = null;
            runUtc = null;
            var list = new List<HourlyRecord>();
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("weather"))
                return list;

            if (root.ContainsKey("sources"))
            {
                var srcArr = root["sources"] as object[];
                if (srcArr != null && srcArr.Length > 0)
                {
                    var s0 = srcArr[0] as Dictionary<string, object>;
                    if (s0 != null && s0.ContainsKey("first_record") && s0["first_record"] != null)
                    {
                        DateTimeOffset dto;
                        if (DateTimeOffset.TryParse(Convert.ToString(s0["first_record"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out dto))
                        {
                            DateTime utc = dto.UtcDateTime;
                            runUtc = utc;
                            runTimeFormatted = string.Format(CultureInfo.InvariantCulture, "{0:dd.MM.yyyy HH:mm} ({1:HH}Z)", utc.ToLocalTime(), utc);
                        }
                    }
                }
            }

            var wArr = root["weather"] as object[];
            if (wArr == null)
                return list;

            foreach (var obj in wArr)
            {
                var w = obj as Dictionary<string, object>;
                if (w == null || !w.ContainsKey("timestamp"))
                    continue;

                DateTimeOffset dto;
                if (!DateTimeOffset.TryParse(Convert.ToString(w["timestamp"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out dto))
                    continue;

                DateTime localHour = dto.LocalDateTime;
                localHour = new DateTime(localHour.Year, localHour.Month, localHour.Day, localHour.Hour, 0, 0);

                double? tempC = ReadDouble(w, "temperature");
                double? rhPct = ReadDouble(w, "relative_humidity");
                double? dewC = ReadDouble(w, "dew_point") ?? ComputeDewPointC(tempC, rhPct);
                double? windKmh = ReadDouble(w, "wind_speed");
                double? gustKmh = ReadDouble(w, "wind_gust_speed");
                double? cloudPct = ReadDouble(w, "cloud_cover");
                double? precipMm = ReadDouble(w, "precipitation");
                double? solarKwh = ReadDouble(w, "solar");
                double? solarWm2 = solarKwh.HasValue ? (double?)Math.Max(0.0, solarKwh.Value * 1000.0) : EstimateSolarRadiation(localHour, cloudPct);
                string cond = w.ContainsKey("condition") && w["condition"] != null ? Convert.ToString(w["condition"], CultureInfo.InvariantCulture) : null;

                double? snowCm = ComputeSnowCm(precipMm, tempC, cond);
                double? wmo = MapBrightSkyConditionToWmo(cond, cloudPct, precipMm);

                list.Add(new HourlyRecord
                {
                    LocalHour = localHour,
                    TempC = tempC,
                    RhPct = rhPct,
                    DewPointC = dewC,
                    WindKmh = windKmh,
                    GustKmh = gustKmh,
                    CloudPct = cloudPct,
                    PrecipMm = precipMm,
                    SnowCm = snowCm,
                    SolarWm2 = solarWm2,
                    WmoCode = wmo
                });
            }

            return list;
        }

        private static double? ComputeSnowCm(double? precipMm, double? tempC, string conditionOrSymbol)
        {
            if (!precipMm.HasValue) return null;
            if (precipMm.Value <= 0.0) return 0.0;

            bool symbolSnow = !string.IsNullOrEmpty(conditionOrSymbol) &&
                              conditionOrSymbol.IndexOf("snow", StringComparison.OrdinalIgnoreCase) >= 0;

            if (symbolSnow || (tempC.HasValue && tempC.Value <= 0.4))
                return Math.Round(precipMm.Value * 1.0, 2);
            if (tempC.HasValue && tempC.Value <= 1.5)
                return Math.Round(precipMm.Value * 0.4, 2);
            return 0.0;
        }

        private static double EstimateSolarRadiation(DateTime localHour, double? cloudPct)
        {
            int h = localHour.Hour;
            if (h < 7 || h > 18) return 0.0;
            double elevFactor = Math.Sin(Math.PI * (h - 6) / 13.0);
            if (elevFactor <= 0) return 0.0;
            double clearSky = 550.0 * elevFactor;
            double cf = cloudPct.HasValue ? Math.Max(0.0, Math.Min(100.0, cloudPct.Value)) / 100.0 : 0.5;
            return Math.Round(clearSky * (1.0 - 0.75 * cf), 1);
        }

        private static double? MapMetNoSymbolToWmo(string symbol, double? cloudPct)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                if (!cloudPct.HasValue) return null;
                if (cloudPct.Value < 15) return 0;
                if (cloudPct.Value < 55) return 1;
                if (cloudPct.Value < 85) return 2;
                return 3;
            }
            string s = symbol.ToLowerInvariant();
            if (s.Contains("thunder")) return 95;
            if (s.Contains("snowshowers")) return 85;
            if (s.Contains("heavysnow")) return 75;
            if (s.Contains("lightsnow")) return 71;
            if (s.Contains("snow")) return 73;
            if (s.Contains("sleet")) return 67;
            if (s.Contains("rainshowers")) return 80;
            if (s.Contains("heavyrain")) return 65;
            if (s.Contains("lightrain")) return 61;
            if (s.Contains("rain")) return 63;
            if (s.Contains("fog")) return 45;
            if (s.Contains("cloudy") && !s.Contains("partly")) return 3;
            if (s.Contains("partlycloudy")) return 2;
            if (s.Contains("fair")) return 1;
            return 0;
        }

        private static double? MapBrightSkyConditionToWmo(string cond, double? cloudPct, double? precipMm)
        {
            if (!string.IsNullOrEmpty(cond))
            {
                string c = cond.ToLowerInvariant();
                if (c == "thunderstorm") return 95;
                if (c == "hail") return 96;
                if (c == "sleet") return 67;
                if (c == "snow") return 73;
                if (c == "fog") return 45;
                if (c == "rain")
                {
                    if (precipMm.HasValue && precipMm.Value >= 4.0) return 65;
                    if (precipMm.HasValue && precipMm.Value < 0.5) return 61;
                    return 63;
                }
            }

            if (!cloudPct.HasValue) return 1;
            if (cloudPct.Value < 15) return 0;
            if (cloudPct.Value < 55) return 1;
            if (cloudPct.Value < 85) return 2;
            return 3;
        }

        private static double? ReadDouble(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null)
                return null;
            return Convert.ToDouble(dict[key], CultureInfo.InvariantCulture);
        }

        private static double? ReadNestedDouble(Dictionary<string, object> dict, string subKey, string leafKey)
        {
            if (dict == null || !dict.ContainsKey(subKey) || dict[subKey] == null)
                return null;
            var sub = dict[subKey] as Dictionary<string, object>;
            return ReadDouble(sub, leafKey);
        }

        private static string ReadNestedString(Dictionary<string, object> dict, string subKey, string leafKey)
        {
            if (dict == null || !dict.ContainsKey(subKey) || dict[subKey] == null)
                return null;
            var sub = dict[subKey] as Dictionary<string, object>;
            if (sub == null || !sub.ContainsKey(leafKey) || sub[leafKey] == null)
                return null;
            return Convert.ToString(sub[leafKey], CultureInfo.InvariantCulture);
        }
    }
}
