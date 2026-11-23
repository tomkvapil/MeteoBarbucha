using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MeteoSrazky
{
    public class WeatherService
    {
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MeteoSrazkyCZ/1.0");
            return client;
        }

        public async Task<ForecastResult> FetchPrecipitationAsync(
            List<WeatherModelInfo> selectedModels,
            int forecastDays,
            string meteologixApiKey,
            Action<string> log)
        {
            return await FetchPrecipitationAsync(selectedModels, forecastDays, null, null, WeatherParameter.TotalPrecipitation, meteologixApiKey, log);
        }

        public async Task<ForecastResult> FetchPrecipitationAsync(
            List<WeatherModelInfo> selectedModels,
            int forecastDays,
            DateTime? startHour,
            DateTime? endHour,
            WeatherParameter parameter,
            string meteologixApiKey,
            Action<string> log,
            CancellationToken ct = default(CancellationToken))
        {
            if (selectedModels == null || selectedModels.Count == 0)
                throw new ArgumentException("Vyberte alespoň jeden meteorologický model.");

            bool useHourly = startHour.HasValue && endHour.HasValue;
            DateTime cleanStart = DateTime.MinValue;
            DateTime cleanEnd = DateTime.MinValue;

            if (useHourly)
            {
                cleanStart = new DateTime(startHour.Value.Year, startHour.Value.Month, startHour.Value.Day, startHour.Value.Hour, 0, 0);
                cleanEnd = new DateTime(endHour.Value.Year, endHour.Value.Month, endHour.Value.Day, endHour.Value.Hour, 0, 0);
                if (cleanEnd <= cleanStart)
                    throw new ArgumentException("Konec časového úseku musí být později než začátek.");
            }

            var paramInfo = CzechData.GetParameterInfo(parameter);
            var result = new ForecastResult
            {
                FetchedAt = DateTime.Now,
                ForecastDays = useHourly ? Math.Max(1, (int)Math.Ceiling((cleanEnd - cleanStart).TotalDays)) : forecastDays,
                Parameter = parameter,
                Unit = paramInfo.Unit,
                Models = new List<WeatherModelInfo>(selectedModels)
            };

            // Build single combined batch: 14 regional centroids + 220 spatial grid points (1 HTTP call!)
            var latList = new List<string>();
            var lonList = new List<string>();
            foreach (var r in CzechData.Regions)
            {
                latList.Add(r.Latitude.ToString("0.0000", CultureInfo.InvariantCulture));
                lonList.Add(r.Longitude.ToString("0.0000", CultureInfo.InvariantCulture));
                result.RegionModelSums[r.Code] = new Dictionary<string, double?>();
            }

            var gridPts = new List<GeoGridPoint>();
            for (double lat = 48.55; lat <= 51.06; lat += 0.25)
            {
                for (double lon = 12.10; lon <= 18.85; lon += 0.35)
                {
                    double rLat = Math.Round(lat, 2);
                    double rLon = Math.Round(lon, 2);
                    latList.Add(rLat.ToString("0.00", CultureInfo.InvariantCulture));
                    lonList.Add(rLon.ToString("0.00", CultureInfo.InvariantCulture));
                    gridPts.Add(new GeoGridPoint
                    {
                        Latitude = rLat,
                        Longitude = rLon,
                        RegionCode = CzechData.FindRegionCode(rLat, rLon)
                    });
                }
            }

            var modelIds = new List<string>();
            foreach (var m in selectedModels)
                modelIds.Add(m.Id);

            string url;
            string sectionKey;
            string varPrefix;

            if (useHourly)
            {
                string sParam = cleanStart.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
                string eParam = cleanEnd.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
                double totalHours = (cleanEnd - cleanStart).TotalHours;
                result.PeriodDescription = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:dd.MM.yyyy HH:00} – {1:dd.MM.yyyy HH:00} ({2:0} h)",
                    cleanStart, cleanEnd, totalHours);

                varPrefix = paramInfo.HourlyVar;
                url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&hourly={2}&models={3}&timezone=Europe%2FPrague&start_hour={4}&end_hour={5}",
                    string.Join(",", latList),
                    string.Join(",", lonList),
                    varPrefix,
                    string.Join(",", modelIds),
                    Uri.EscapeDataString(sParam),
                    Uri.EscapeDataString(eParam)
                );
                sectionKey = "hourly";
            }
            else if (paramInfo.ForceHourly)
            {
                varPrefix = paramInfo.HourlyVar;
                url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&hourly={2}&models={3}&timezone=Europe%2FPrague&forecast_days={4}",
                    string.Join(",", latList),
                    string.Join(",", lonList),
                    varPrefix,
                    string.Join(",", modelIds),
                    forecastDays
                );
                sectionKey = "hourly";
            }
            else
            {
                varPrefix = paramInfo.DailyVar;
                url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily={2}&models={3}&timezone=Europe%2FPrague&forecast_days={4}",
                    string.Join(",", latList),
                    string.Join(",", lonList),
                    varPrefix,
                    string.Join(",", modelIds),
                    forecastDays
                );
                sectionKey = "daily";
            }

            string json = DiskCache.TryGet(url);
            if (!string.IsNullOrEmpty(json))
            {
                log("Načteno z 12h diskové mezipaměti (0 síťových požadavků): " + paramInfo.DisplayName);
            }
            else
            {
                log("Stahuji data — " + paramInfo.DisplayName + " (" + latList.Count + " bodů × " + selectedModels.Count + " modelů v 1 požadavku)...");
                json = await GetStringWithRetryAsync(url, ct, log);
                DiskCache.Put(url, json);
            }

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            object parsed = serializer.DeserializeObject(json);

            object[] items = parsed as object[];
            if (items == null)
                items = new object[] { parsed };

            int regionCount = CzechData.Regions.Count;
            bool singleModel = selectedModels.Count == 1;

            for (int i = 0; i < Math.Min(items.Length, regionCount); i++)
            {
                var region = CzechData.Regions[i];
                var itemDict = items[i] as Dictionary<string, object>;
                if (itemDict == null || !itemDict.ContainsKey(sectionKey))
                    continue;

                var section = itemDict[sectionKey] as Dictionary<string, object>;
                if (section == null)
                    continue;

                if (i == 0 && section.ContainsKey("time"))
                {
                    var timeArr = section["time"] as object[];
                    if (timeArr != null)
                    {
                        foreach (var t in timeArr)
                        {
                            string ts = Convert.ToString(t, CultureInfo.InvariantCulture);
                            result.Dates.Add(ts);
                            result.TimeSteps.Add(ts);
                        }
                    }
                }

                foreach (var model in selectedModels)
                {
                    result.RegionModelSums[region.Code][model.Id] = ExtractModelValue(
                        section, model.Id, singleModel, paramInfo, varPrefix);
                }
            }

            for (int gi = 0; gi < gridPts.Count && (regionCount + gi) < items.Length; gi++)
            {
                var itemDict = items[regionCount + gi] as Dictionary<string, object>;
                if (itemDict == null || !itemDict.ContainsKey(sectionKey))
                    continue;

                var section = itemDict[sectionKey] as Dictionary<string, object>;
                if (section == null)
                    continue;

                foreach (var model in selectedModels)
                {
                    gridPts[gi].ModelValues[model.Id] = ExtractModelValue(section, model.Id, singleModel, paramInfo, varPrefix);
                    gridPts[gi].ModelSeries[model.Id] = ExtractModelSeries(section, model.Id, singleModel, paramInfo, varPrefix);
                }
            }
            result.GridPoints = gridPts;

            if (!useHourly && result.Dates.Count > 0)
            {
                string firstDate = result.Dates[0];
                string lastDate = result.Dates[result.Dates.Count - 1];
                if (firstDate.Length > 10) firstDate = firstDate.Substring(0, 10);
                if (lastDate.Length > 10) lastDate = lastDate.Substring(0, 10);
                result.PeriodDescription = firstDate + " až " + lastDate + " (" + forecastDays + " dny)";
            }

            // Fetch model run initialization times from static CDN metadata
            await FetchModelRunTimesAsync(selectedModels, result, ct);

            log("Open-Meteo data úspěšně zpracována (" + gridPts.Count + " bodů sítě).");

            // Optional Meteologix / Kachelmannwetter API check if key is provided
            if (!string.IsNullOrWhiteSpace(meteologixApiKey))
            {
                await TryFetchMeteologixApiAsync(meteologixApiKey.Trim(), result, useHourly ? (DateTime?)cleanStart : null, useHourly ? (DateTime?)cleanEnd : null, log);
            }

            return result;
        }

        public static double? GetGridPointValue(GeoGridPoint gp, string modelId, int timeStepIndex)
        {
            if (gp == null || string.IsNullOrEmpty(modelId)) return null;
            if (timeStepIndex >= 0)
            {
                double?[] series;
                if (gp.ModelSeries != null && gp.ModelSeries.TryGetValue(modelId, out series) && series != null && timeStepIndex < series.Length)
                    return series[timeStepIndex];
                return null;
            }
            double? v;
            return gp.ModelValues.TryGetValue(modelId, out v) ? v : null;
        }

        public static Dictionary<string, Dictionary<string, double?>> ComputeRegionalTable(ForecastResult result, RegionAggregationMode mode)
        {
            if ((mode == RegionAggregationMode.PointValue && result.SelectedTimeStepIndex < 0) || result.GridPoints == null || result.GridPoints.Count == 0)
                return result.RegionModelSums;

            var table = new Dictionary<string, Dictionary<string, double?>>();
            foreach (var r in CzechData.Regions)
            {
                table[r.Code] = new Dictionary<string, double?>();
                foreach (var m in result.Models)
                {
                    double sum = 0;
                    double max = double.MinValue;
                    int cnt = 0;

                    foreach (var gp in result.GridPoints)
                    {
                        if (gp.RegionCode != r.Code) continue;
                        double? v = GetGridPointValue(gp, m.Id, result.SelectedTimeStepIndex);
                        if (v.HasValue)
                        {
                            sum += v.Value;
                            if (v.Value > max) max = v.Value;
                            cnt++;
                        }
                    }

                    if (cnt > 0)
                        table[r.Code][m.Id] = Math.Round(mode == RegionAggregationMode.RegionMax ? max : (sum / cnt), 2);
                    else if (result.SelectedTimeStepIndex < 0)
                    {
                        double? fallback;
                        result.RegionModelSums[r.Code].TryGetValue(m.Id, out fallback);
                        table[r.Code][m.Id] = fallback;
                    }
                    else
                    {
                        table[r.Code][m.Id] = null;
                    }
                }
            }
            return table;
        }

        private static double? ExtractModelValue(
            Dictionary<string, object> section,
            string modelId,
            bool isSingleModel,
            WeatherParameterInfo paramInfo,
            string varPrefix)
        {
            if (paramInfo.Parameter == WeatherParameter.UtciMax)
                return ComputeMaxUtciForModel(section, modelId, isSingleModel);

            string key = varPrefix + "_" + modelId;
            if (!section.ContainsKey(key) && isSingleModel && section.ContainsKey(varPrefix))
                key = varPrefix;

            return section.ContainsKey(key)
                ? AggregateNumericArray(section[key] as object[], paramInfo.Aggregation)
                : null;
        }

        private static double? ComputeMaxUtciForModel(Dictionary<string, object> section, string modelId, bool isSingleModel)
        {
            object[] taArr = GetVarArray(section, "temperature_2m", modelId, isSingleModel);
            object[] rhArr = GetVarArray(section, "relative_humidity_2m", modelId, isSingleModel);
            object[] wsArr = GetVarArray(section, "wind_speed_10m", modelId, isSingleModel);
            object[] swArr = GetVarArray(section, "shortwave_radiation", modelId, isSingleModel);

            if (taArr == null || rhArr == null || wsArr == null)
                return null;

            int len = Math.Min(taArr.Length, Math.Min(rhArr.Length, wsArr.Length));
            if (swArr != null)
                len = Math.Min(len, swArr.Length);

            double maxUtci = double.MinValue;
            bool found = false;

            for (int i = 0; i < len; i++)
            {
                if (taArr[i] == null || rhArr[i] == null || wsArr[i] == null)
                    continue;

                double ta = Convert.ToDouble(taArr[i], CultureInfo.InvariantCulture);
                double rh = Convert.ToDouble(rhArr[i], CultureInfo.InvariantCulture);
                double ws = Convert.ToDouble(wsArr[i], CultureInfo.InvariantCulture);
                double sw = (swArr != null && swArr[i] != null) ? Convert.ToDouble(swArr[i], CultureInfo.InvariantCulture) : 0.0;

                double utci = UtciCalculator.Compute(ta, rh, ws, sw);
                if (utci > maxUtci) maxUtci = utci;
                found = true;
            }

            return found ? (double?)Math.Round(maxUtci, 2) : null;
        }

        private static object[] GetVarArray(Dictionary<string, object> section, string baseVar, string modelId, bool isSingleModel)
        {
            string key = baseVar + "_" + modelId;
            if (!section.ContainsKey(key) && isSingleModel && section.ContainsKey(baseVar))
                key = baseVar;
            return section.ContainsKey(key) ? section[key] as object[] : null;
        }

        private static double? AggregateNumericArray(object[] values, TimeAggregation agg)
        {
            if (values == null)
                return null;

            double sum = 0;
            double max = double.MinValue;
            double min = double.MaxValue;
            int cnt = 0;

            foreach (var v in values)
            {
                if (v != null)
                {
                    double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                    sum += d;
                    if (d > max) max = d;
                    if (d < min) min = d;
                    cnt++;
                }
            }

            if (cnt == 0) return null;
            double res;
            switch (agg)
            {
                case TimeAggregation.Max: res = max; break;
                case TimeAggregation.Min: res = min; break;
                case TimeAggregation.Mean: res = sum / cnt; break;
                default: res = sum; break;
            }
            return Math.Round(res, 2);
        }

        private static double?[] ExtractModelSeries(
            Dictionary<string, object> section,
            string modelId,
            bool isSingleModel,
            WeatherParameterInfo paramInfo,
            string varPrefix)
        {
            if (paramInfo.Parameter == WeatherParameter.UtciMax)
            {
                object[] taArr = GetVarArray(section, "temperature_2m", modelId, isSingleModel);
                object[] rhArr = GetVarArray(section, "relative_humidity_2m", modelId, isSingleModel);
                object[] wsArr = GetVarArray(section, "wind_speed_10m", modelId, isSingleModel);
                object[] swArr = GetVarArray(section, "shortwave_radiation", modelId, isSingleModel);
                if (taArr == null || rhArr == null || wsArr == null) return null;

                int len = Math.Min(taArr.Length, Math.Min(rhArr.Length, wsArr.Length));
                if (swArr != null) len = Math.Min(len, swArr.Length);
                var arr = new double?[len];
                for (int i = 0; i < len; i++)
                {
                    if (taArr[i] == null || rhArr[i] == null || wsArr[i] == null) continue;
                    double ta = Convert.ToDouble(taArr[i], CultureInfo.InvariantCulture);
                    double rh = Convert.ToDouble(rhArr[i], CultureInfo.InvariantCulture);
                    double ws = Convert.ToDouble(wsArr[i], CultureInfo.InvariantCulture);
                    double sw = (swArr != null && swArr[i] != null) ? Convert.ToDouble(swArr[i], CultureInfo.InvariantCulture) : 0.0;
                    arr[i] = Math.Round(UtciCalculator.Compute(ta, rh, ws, sw), 2);
                }
                return arr;
            }

            object[] raw = GetVarArray(section, varPrefix, modelId, isSingleModel);
            if (raw == null) return null;
            var outArr = new double?[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != null)
                    outArr[i] = Math.Round(Convert.ToDouble(raw[i], CultureInfo.InvariantCulture), 2);
            }
            return outArr;
        }

        private static async Task<string> GetStringWithRetryAsync(string url, CancellationToken ct, Action<string> log)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using (var resp = await _httpClient.GetAsync(url, ct))
                {
                    if ((int)resp.StatusCode == 429)
                    {
                        if (attempt == 0)
                        {
                            int waitSec = 3;
                            if (resp.Headers.RetryAfter != null && resp.Headers.RetryAfter.Delta.HasValue)
                                waitSec = Math.Max(1, Math.Min(15, (int)resp.Headers.RetryAfter.Delta.Value.TotalSeconds));
                            log("Open-Meteo API vrátilo 429 (Too Many Requests). Čekám " + waitSec + " s a opakuji pokus...");
                            await Task.Delay(waitSec * 1000, ct);
                            continue;
                        }
                        throw new Exception("Překročen limit bezplatného Open-Meteo API (429 Too Many Requests). Vyčkejte prosím cca 30–60 sekund nebo vyberte méně modelů.");
                    }
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync();
                }
            }
            throw new Exception("Překročen limit bezplatného Open-Meteo API (429).");
        }

        private static async Task FetchModelRunTimesAsync(List<WeatherModelInfo> models, ForecastResult result, CancellationToken ct)
        {
            var tasks = new List<Task>();
            var lockObj = new object();

            foreach (var m in models)
            {
                var model = m;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string metaUrl = "https://api.open-meteo.com/data/" + model.Id + "/static/meta.json";
                        string metaJson = await _httpClient.GetStringAsync(metaUrl);
                        var ser = new JavaScriptSerializer();
                        var dict = ser.DeserializeObject(metaJson) as Dictionary<string, object>;
                        if (dict != null && dict.ContainsKey("last_run_initialisation_time") && dict["last_run_initialisation_time"] != null)
                        {
                            long unixSec = Convert.ToInt64(dict["last_run_initialisation_time"], CultureInfo.InvariantCulture);
                            DateTime utcRun = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSec);
                            DateTime localRun = utcRun.ToLocalTime();
                            string formatted = string.Format(CultureInfo.InvariantCulture, "{0:dd.MM.yyyy HH:mm} ({1:HH}Z)", localRun, utcRun);
                            lock (lockObj)
                            {
                                result.ModelRunTimes[model.Id] = formatted;
                            }
                        }
                    }
                    catch
                    {
                        // Non-critical metadata lookup
                    }
                }, ct));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
            }
        }

        private async Task TryFetchMeteologixApiAsync(string apiKey, ForecastResult result, DateTime? startHour, DateTime? endHour, Action<string> log)
        {
            log("Ověřuji volitelný Meteologix / Kachelmannwetter API klíč...");
            var kachelmannModels = new[]
            {
                new { EndpointSlug = "ecmwf", Info = new WeatherModelInfo { Id = "meteologix_ecmwf", DisplayName = "Meteologix ECMWF (API)", Provider = "Kachelmannwetter" } },
                new { EndpointSlug = "swiss-hd", Info = new WeatherModelInfo { Id = "meteologix_swiss4x4", DisplayName = "Europe Swiss 4x4 (API)", Provider = "Kachelmannwetter" } },
                new { EndpointSlug = "swiss-nowcast", Info = new WeatherModelInfo { Id = "meteologix_swiss_nowcast", DisplayName = "Europe Swiss Nowcast HD (API)", Provider = "Kachelmannwetter" } }
            };

            try
            {
                foreach (var km in kachelmannModels)
                {
                    var sumsByRegion = new Dictionary<string, double?>();
                    int successCount = 0;

                    foreach (var region in CzechData.Regions)
                    {
                        string endpoint = string.Format(
                            CultureInfo.InvariantCulture,
                            "https://api.kachelmannwetter.com/v02/forecast/{0:0.0000}/{1:0.0000}/trend/{2}",
                            region.Latitude,
                            region.Longitude,
                            km.EndpointSlug
                        );

                        using (var req = new HttpRequestMessage(HttpMethod.Get, endpoint))
                        {
                            req.Headers.Add("X-API-Key", apiKey);
                            using (var resp = await _httpClient.SendAsync(req))
                            {
                                if (!resp.IsSuccessStatusCode)
                                {
                                    if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                                    {
                                        log("Meteologix API vrátilo stav " + (int)resp.StatusCode + ". Zkontrolujte platnost vašeho X-API-Key klíče.");
                                        return;
                                    }
                                    sumsByRegion[region.Code] = null;
                                    continue;
                                }

                                string body = await resp.Content.ReadAsStringAsync();
                                double? total = ParseKachelmannPrecipSum(body, result.ForecastDays, startHour, endHour);
                                sumsByRegion[region.Code] = total;
                                if (total.HasValue)
                                    successCount++;
                            }
                        }
                    }

                    if (successCount > 0)
                    {
                        result.Models.Add(km.Info);
                        foreach (var region in CzechData.Regions)
                        {
                            double? val;
                            sumsByRegion.TryGetValue(region.Code, out val);
                            result.RegionModelSums[region.Code][km.Info.Id] = val;
                        }
                        log(km.Info.DisplayName + " připojeno (" + successCount + "/14 krajů).");
                    }
                }
            }
            catch (Exception ex)
            {
                log("Chyba při komunikaci s Meteologix API: " + ex.Message);
            }
        }

        private double? ParseKachelmannPrecipSum(string json, int forecastDays, DateTime? startHour, DateTime? endHour)
        {
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("data"))
                return null;

            var dataArr = root["data"] as object[];
            if (dataArr == null)
                return null;

            DateTime cutoff = DateTime.UtcNow.AddDays(forecastDays);
            double sum = 0;
            bool found = false;

            foreach (var item in dataArr)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null)
                    continue;

                if (entry.ContainsKey("dateTime"))
                {
                    DateTime dt;
                    if (DateTime.TryParse(Convert.ToString(entry["dateTime"], CultureInfo.InvariantCulture), out dt))
                    {
                        if (startHour.HasValue && endHour.HasValue)
                        {
                            DateTime localDt = dt.ToLocalTime();
                            if (localDt < startHour.Value || localDt > endHour.Value)
                                continue;
                        }
                        else if (dt.ToUniversalTime() > cutoff)
                        {
                            continue;
                        }
                    }
                }

                if (entry.ContainsKey("prec") && entry["prec"] != null)
                {
                    sum += Convert.ToDouble(entry["prec"], CultureInfo.InvariantCulture);
                    found = true;
                }
                else if (entry.ContainsKey("precipitation") && entry["precipitation"] != null)
                {
                    sum += Convert.ToDouble(entry["precipitation"], CultureInfo.InvariantCulture);
                    found = true;
                }
            }

            return found ? (double?)Math.Round(sum, 2) : null;
        }
    }
}
