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

        private class HttpRateLimitException : Exception
        {
            public HttpRateLimitException(string message) : base(message) { }
        }

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MeteoBarbuchaCZ/1.4.0");
            return client;
        }

        public static string BuildOpenMeteoForecastUrl(string queryParams, string customKeyOrUrl)
        {
            if (string.IsNullOrWhiteSpace(customKeyOrUrl))
                return "https://api.open-meteo.com/v1/forecast?" + queryParams;

            string trimmed = customKeyOrUrl.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.TrimEnd('/', '?');
                if (trimmed.IndexOf("/forecast", StringComparison.OrdinalIgnoreCase) < 0)
                    trimmed = trimmed + "/v1/forecast";
                return trimmed + "?" + queryParams;
            }

            return "https://customer-api.open-meteo.com/v1/forecast?" + queryParams + "&apikey=" + Uri.EscapeDataString(trimmed);
        }

        public async Task<ForecastResult> FetchPrecipitationAsync(
            List<WeatherModelInfo> selectedModels,
            int forecastDays,
            string meteologixApiKey,
            Action<string> log)
        {
            return await FetchPrecipitationAsync(selectedModels, forecastDays, null, null, WeatherParameter.TotalPrecipitation, meteologixApiKey, log);
        }

        public static async Task<Tuple<bool, string>> VerifyMeteologixApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return Tuple.Create(false, "Zadejte nejprve API klíč.");

            string key = apiKey.Trim();
            try
            {
                string authUrl = "https://api.kachelmannwetter.com/v02/tools/astronomy/50.0755/14.4378";
                using (var req = new HttpRequestMessage(HttpMethod.Get, authUrl))
                {
                    req.Headers.Add("X-API-Key", key);
                    using (var resp = await _httpClient.SendAsync(req))
                    {
                        if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                            return Tuple.Create(false, "✗ Neplatný nebo neautorizovaný klíč (" + (int)resp.StatusCode + ")");
                        if (!resp.IsSuccessStatusCode)
                            return Tuple.Create(false, "✗ Chyba API: HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                    }
                }

                string forecastUrl = "https://api.kachelmannwetter.com/v02/forecast/50.0755/14.4378/3day";
                using (var req2 = new HttpRequestMessage(HttpMethod.Get, forecastUrl))
                {
                    req2.Headers.Add("X-API-Key", key);
                    using (var resp2 = await _httpClient.SendAsync(req2))
                    {
                        if (resp2.IsSuccessStatusCode)
                            return Tuple.Create(true, "✓ Klíč je platný a předpověď je aktivní (200 OK)");
                        if ((int)resp2.StatusCode == 403)
                            return Tuple.Create(true, "✓ Klíč platný, ale Praha není povolena (403 — načtou se povolené lokality)");
                        return Tuple.Create(true, "✓ Klíč platný (předpověď: HTTP " + (int)resp2.StatusCode + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                return Tuple.Create(false, "✗ Chyba připojení: " + ex.Message);
            }
        }

        public async Task<ForecastResult> FetchPrecipitationAsync(
            List<WeatherModelInfo> selectedModels,
            int forecastDays,
            DateTime? startHour,
            DateTime? endHour,
            WeatherParameter parameter,
            string meteologixApiKey,
            Action<string> log,
            CancellationToken ct = default(CancellationToken),
            DataSourcePreference dataSource = DataSourcePreference.AutoMultiSource,
            string openMeteoCustomKeyOrUrl = null,
            Action<int, int, string> progress = null)
        {
            if (selectedModels == null || selectedModels.Count == 0)
            {
                if (dataSource != DataSourcePreference.AlternativeOpenOnly && dataSource != DataSourcePreference.MeteologixOnly)
                    throw new ArgumentException("Vyberte alespoň jeden meteorologický model.");
            }

            if (dataSource == DataSourcePreference.MeteologixOnly && string.IsNullOrWhiteSpace(meteologixApiKey))
                throw new ArgumentException("Pro režim 'Pouze Meteologix API' musíte v Nastavení zadat platný X-API-Key klíč.");

            bool useCustomHours = startHour.HasValue && endHour.HasValue;
            DateTime nowLocal = DateTime.Now;
            DateTime nowHour = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, 0, 0);

            DateTime cleanStart;
            DateTime cleanEnd;

            if (useCustomHours)
            {
                cleanStart = new DateTime(startHour.Value.Year, startHour.Value.Month, startHour.Value.Day, startHour.Value.Hour, 0, 0);
                cleanEnd = new DateTime(endHour.Value.Year, endHour.Value.Month, endHour.Value.Day, endHour.Value.Hour, 0, 0);
                if (cleanEnd <= cleanStart)
                    throw new ArgumentException("Konec časového úseku musí být později než začátek.");
            }
            else
            {
                // Unified future forecast window [nowHour .. nowHour + forecastDays*24 - 1] so all providers compare identical hours!
                cleanStart = nowHour;
                cleanEnd = nowHour.AddHours(forecastDays * 24 - 1);
            }

            var paramInfo = CzechData.GetParameterInfo(parameter);
            var result = new ForecastResult
            {
                FetchedAt = DateTime.Now,
                ForecastDays = useCustomHours ? Math.Max(1, (int)Math.Ceiling((cleanEnd - cleanStart).TotalDays)) : forecastDays,
                Parameter = parameter,
                Unit = paramInfo.Unit,
                Models = new List<WeatherModelInfo>(),
                DataSourceLabel = "",
                PeriodDescription = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:dd.MM.yyyy HH:00} – {1:dd.MM.yyyy HH:00} ({2:0} h)",
                    cleanStart, cleanEnd.AddHours(1), (cleanEnd - cleanStart).TotalHours + 1)
            };

            foreach (var r in CzechData.Regions)
                result.RegionModelSums[r.Code] = new Dictionary<string, double?>();

            // 1) Meteologix-only mode
            if (dataSource == DataSourcePreference.MeteologixOnly)
            {
                await TryFetchMeteologixApiAsync(meteologixApiKey.Trim(), result, paramInfo, cleanStart, cleanEnd, log, progress, ct).ConfigureAwait(false);
                if (result.Models.Count == 0)
                    throw new Exception("Meteologix API nevrátilo žádná data pro žádný z krajů ČR. Ověřte platnost API klíče a povolené lokality na accounts.meteologix.com.");
                result.DataSourceLabel = "Meteologix API (14 bodů)";
                EnsembleStats.SnapshotBaseValues(result);
                return result;
            }

            var openMeteoModels = new List<WeatherModelInfo>();
            var altModels = new List<WeatherModelInfo>();
            if (selectedModels != null)
            {
                foreach (var m in selectedModels)
                {
                    if (CzechData.IsOpenMeteoModel(m.Id))
                        openMeteoModels.Add(m);
                    else if (m.Id == AlternativeWeatherProviders.MetNoId || m.Id == AlternativeWeatherProviders.BrightSkyId)
                        altModels.Add(m);
                }
            }

            // 2) Alternative open sources only (MET Norway + Bright Sky — 0 Open-Meteo requests)
            if (dataSource == DataSourcePreference.AlternativeOpenOnly)
            {
                if (altModels.Count == 0)
                    altModels = AlternativeWeatherProviders.GetDefaultAlternativeModels();

                await AlternativeWeatherProviders.PopulateAlternativeModelsAsync(
                    altModels, result, cleanStart, cleanEnd, paramInfo, log, progress, ct).ConfigureAwait(false);

                var srcParts = new List<string> { "MET Norway + Bright Sky" };
                if (!string.IsNullOrWhiteSpace(meteologixApiKey))
                {
                    int before = result.Models.Count;
                    await TryFetchMeteologixApiAsync(meteologixApiKey.Trim(), result, paramInfo, cleanStart, cleanEnd, log, progress, ct).ConfigureAwait(false);
                    if (result.Models.Count > before)
                        srcParts.Add("Meteologix API");
                }

                if (result.Models.Count == 0)
                    throw new Exception("Alternativní zdroje (MET Norway / Bright Sky) nevrátily žádná data.");

                result.DataSourceLabel = string.Join(" + ", srcParts);
                EnsembleStats.SnapshotBaseValues(result);
                return result;
            }

            if (dataSource == DataSourcePreference.OpenMeteoOnly && openMeteoModels.Count == 0)
                throw new ArgumentException("Pro režim 'Pouze Open-Meteo API' zaškrtněte alespoň jeden model Open-Meteo.");

            var activeSources = new List<string>();
            bool openMeteoRateLimited = false;

            // 3) Open-Meteo batch fetch (182 points: 14 regional capitals + 168 Czechia & border points, ~22% fewer points than 234!)
            if (openMeteoModels.Count > 0)
            {
                try
                {
                    if (progress != null)
                        progress(10, 100, "Stahuji dávku Open-Meteo (" + openMeteoModels.Count + " modelů)...");

                    await FetchOpenMeteoBatchAsync(
                        openMeteoModels, result, cleanStart, cleanEnd,
                        paramInfo, openMeteoCustomKeyOrUrl, log, ct).ConfigureAwait(false);
                    activeSources.Add(string.IsNullOrWhiteSpace(openMeteoCustomKeyOrUrl) ? "Open-Meteo" : "Open-Meteo (vlastní)");

                    if (progress != null)
                        progress(60, 100, "Zpracována dávka Open-Meteo.");
                }
                catch (HttpRateLimitException ex429)
                {
                    AppLogger.Warn("Open-Meteo HTTP 429: " + ex429.Message);
                    if (dataSource == DataSourcePreference.OpenMeteoOnly)
                        throw new Exception(ex429.Message);

                    openMeteoRateLimited = true;
                    log("⚠ Open-Meteo vrátilo limit 429 — automaticky přepínám na otevřené záložní zdroje (MET Norway + Bright Sky)!");
                    if (altModels.Count == 0)
                        altModels = AlternativeWeatherProviders.GetDefaultAlternativeModels();
                }
            }

            // 4) Alternative open providers (MET Norway + Bright Sky) when checked or triggered as 429 fallback
            if (dataSource != DataSourcePreference.OpenMeteoOnly && altModels.Count > 0)
            {
                int beforeAlt = result.Models.Count;
                await AlternativeWeatherProviders.PopulateAlternativeModelsAsync(
                    altModels, result, cleanStart, cleanEnd, paramInfo, log, progress, ct).ConfigureAwait(false);
                if (result.Models.Count > beforeAlt)
                    activeSources.Add(openMeteoRateLimited ? "Záloha: MET Norway + Bright Sky" : "MET Norway / Bright Sky");
            }

            // 5) Optional Meteologix / Kachelmannwetter API
            if (dataSource != DataSourcePreference.OpenMeteoOnly && !string.IsNullOrWhiteSpace(meteologixApiKey))
            {
                int beforeMlx = result.Models.Count;
                await TryFetchMeteologixApiAsync(meteologixApiKey.Trim(), result, paramInfo, cleanStart, cleanEnd, log, progress, ct).ConfigureAwait(false);
                if (result.Models.Count > beforeMlx)
                    activeSources.Add("Meteologix API");
            }

            if (result.Models.Count == 0)
                throw new Exception("Žádný ze zvolených zdrojů dat nevrátil platná data.");

            result.DataSourceLabel = activeSources.Count > 0 ? string.Join(" + ", activeSources) : "Open-Meteo API";
            EnsembleStats.SnapshotBaseValues(result);
            return result;
        }

        private async Task FetchOpenMeteoBatchAsync(
            List<WeatherModelInfo> openMeteoModels,
            ForecastResult result,
            DateTime cleanStart,
            DateTime cleanEnd,
            WeatherParameterInfo paramInfo,
            string openMeteoCustomKeyOrUrl,
            Action<string> log,
            CancellationToken ct)
        {
            var latList = new List<string>();
            var lonList = new List<string>();
            foreach (var r in CzechData.Regions)
            {
                latList.Add(r.Latitude.ToString("0.0000", CultureInfo.InvariantCulture));
                lonList.Add(r.Longitude.ToString("0.0000", CultureInfo.InvariantCulture));
            }

            var gridPts = new List<GeoGridPoint>();
            for (double lat = 48.55; lat <= 51.06; lat += 0.25)
            {
                for (double lon = 12.10; lon <= 18.85; lon += 0.35)
                {
                    double rLat = Math.Round(lat, 2);
                    double rLon = Math.Round(lon, 2);
                    if (!CzechData.IsNearOrInsideCzechia(rLat, rLon, 0.26))
                        continue;

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
            foreach (var m in openMeteoModels)
                modelIds.Add(m.Id);

            string sParam = cleanStart.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
            string eParam = cleanEnd.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
            string varPrefix = paramInfo.HourlyVar;
            string sectionKey = "hourly";

            string queryParams = string.Format(
                CultureInfo.InvariantCulture,
                "latitude={0}&longitude={1}&hourly={2}&models={3}&timezone=Europe%2FPrague&start_hour={4}&end_hour={5}",
                string.Join(",", latList),
                string.Join(",", lonList),
                varPrefix,
                string.Join(",", modelIds),
                Uri.EscapeDataString(sParam),
                Uri.EscapeDataString(eParam)
            );

            string url = BuildOpenMeteoForecastUrl(queryParams, openMeteoCustomKeyOrUrl);

            string json = DiskCache.TryGet(url);
            if (!string.IsNullOrEmpty(json))
            {
                log("Open-Meteo načteno z 12h diskové mezipaměti (0 síťových dotazů): " + paramInfo.DisplayName);
            }
            else
            {
                log("Stahuji Open-Meteo — " + paramInfo.DisplayName + " (" + latList.Count + " bodů ČR × " + openMeteoModels.Count + " modelů v 1 požadavku)...");
                try
                {
                    json = await GetStringWithRetryAsync(url, ct, log).ConfigureAwait(false);
                    DiskCache.Put(url, json);
                }
                catch (Exception netEx)
                {
                    if (ct.IsCancellationRequested || netEx is HttpRateLimitException)
                        throw;

                    // Offline fallback from expired disk cache if available
                    DateTime staleAt;
                    string staleJson = DiskCache.TryGetStale(url, out staleAt);
                    if (!string.IsNullOrEmpty(staleJson))
                    {
                        json = staleJson;
                        result.IsOfflineFallback = true;
                        result.OfflineCachedAt = staleAt;
                        string warnMsg = "⚠ Síť nedostupná (" + netEx.Message + ") — zobrazena OFFLINE záloha z mezipaměti ze dne " + staleAt.ToString("dd.MM.yyyy HH:mm") + ".";
                        log(warnMsg);
                        AppLogger.Warn(warnMsg);
                    }
                    else
                    {
                        AppLogger.Error("Chyba Open-Meteo: " + netEx.Message);
                        throw;
                    }
                }
            }

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            object parsed = serializer.DeserializeObject(json);

            object[] items = parsed as object[];
            if (items == null)
                items = new object[] { parsed };

            int regionCount = CzechData.Regions.Count;
            bool singleModel = openMeteoModels.Count == 1;

            foreach (var m in openMeteoModels)
                result.Models.Add(m);

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
                        result.Dates.Clear();
                        result.TimeSteps.Clear();
                        foreach (var t in timeArr)
                        {
                            string ts = Convert.ToString(t, CultureInfo.InvariantCulture);
                            result.Dates.Add(ts);
                            result.TimeSteps.Add(ts);
                        }
                    }
                }

                foreach (var model in openMeteoModels)
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

                foreach (var model in openMeteoModels)
                {
                    gridPts[gi].ModelValues[model.Id] = ExtractModelValue(section, model.Id, singleModel, paramInfo, varPrefix);
                    gridPts[gi].ModelSeries[model.Id] = ExtractModelSeries(section, model.Id, singleModel, paramInfo, varPrefix);
                }
            }
            result.GridPoints = gridPts;

            await FetchModelRunTimesAsync(openMeteoModels, result, log, ct).ConfigureAwait(false);
            log("Open-Meteo data zpracována (" + gridPts.Count + " bodů sítě ČR + příhraničí).");
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
            bool belowWarn = EnsembleStats.IsBelowThresholdWarning(result.Parameter);

            foreach (var r in CzechData.Regions)
            {
                table[r.Code] = new Dictionary<string, double?>();
                foreach (var m in result.Models)
                {
                    double sum = 0;
                    double max = double.MinValue;
                    double min = double.MaxValue;
                    int cnt = 0;

                    foreach (var gp in result.GridPoints)
                    {
                        if (gp.RegionCode != r.Code) continue;
                        double? v = GetGridPointValue(gp, m.Id, result.SelectedTimeStepIndex);
                        if (v.HasValue)
                        {
                            sum += v.Value;
                            if (v.Value > max) max = v.Value;
                            if (v.Value < min) min = v.Value;
                            cnt++;
                        }
                    }

                    if (cnt > 0)
                    {
                        double extremum = belowWarn ? min : max;
                        table[r.Code][m.Id] = Math.Round(mode == RegionAggregationMode.RegionMax ? extremum : (sum / cnt), 2);
                    }
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
                using (var resp = await _httpClient.GetAsync(url, ct).ConfigureAwait(false))
                {
                    if ((int)resp.StatusCode == 429)
                    {
                        if (attempt == 0)
                        {
                            int waitSec = 3;
                            if (resp.Headers.RetryAfter != null && resp.Headers.RetryAfter.Delta.HasValue)
                                waitSec = Math.Max(1, Math.Min(12, (int)resp.Headers.RetryAfter.Delta.Value.TotalSeconds));
                            log("Open-Meteo API vrátilo 429 (Too Many Requests). Čekám " + waitSec + " s a opakuji pokus...");
                            await Task.Delay(waitSec * 1000, ct).ConfigureAwait(false);
                            continue;
                        }
                        throw new HttpRateLimitException("Překročen limit bezplatného Open-Meteo API (429 Too Many Requests). Zkuste v Nastavení režim 'Alternativní zdroje (MET Norway + Bright Sky)' nebo vlastní klíč.");
                    }
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            throw new HttpRateLimitException("Překročen limit bezplatného Open-Meteo API (429).");
        }

        private static async Task FetchModelRunTimesAsync(List<WeatherModelInfo> models, ForecastResult result, Action<string> log, CancellationToken ct)
        {
            var tasks = new List<Task>();
            var lockObj = new object();
            TimeSpan metaTtl = TimeSpan.FromMinutes(30);
            var staleWarnings = new List<string>();

            foreach (var m in models)
            {
                var model = m;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string metaUrl = "https://api.open-meteo.com/data/" + model.Id + "/static/meta.json";
                        string metaJson = DiskCache.TryGet(metaUrl, metaTtl);
                        if (string.IsNullOrEmpty(metaJson))
                        {
                            metaJson = await _httpClient.GetStringAsync(metaUrl).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(metaJson))
                                DiskCache.Put(metaUrl, metaJson);
                        }

                        var ser = new JavaScriptSerializer();
                        var dict = ser.DeserializeObject(metaJson) as Dictionary<string, object>;
                        if (dict != null && dict.ContainsKey("last_run_initialisation_time") && dict["last_run_initialisation_time"] != null)
                        {
                            long unixSec = Convert.ToInt64(dict["last_run_initialisation_time"], CultureInfo.InvariantCulture);
                            DateTime utcRun = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSec);
                            DateTime localRun = utcRun.ToLocalTime();
                            string formatted = string.Format(CultureInfo.InvariantCulture, "{0:dd.MM.yyyy HH:mm} ({1:HH}Z)", localRun, utcRun);
                            double ageH = (DateTime.UtcNow - utcRun).TotalHours;

                            lock (lockObj)
                            {
                                result.ModelRunTimes[model.Id] = formatted;
                                result.ModelRunUtcTimes[model.Id] = utcRun;
                                if (ageH > 12.0)
                                    staleWarnings.Add(model.DisplayName + " (" + Math.Round(ageH, 1).ToString(CultureInfo.InvariantCulture) + " h starý běh)");
                            }
                        }
                    }
                    catch
                    {
                    }
                }, ct));
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }

            if (staleWarnings.Count > 0)
            {
                string msg = "⚠ Upozornění na starší běh modelu (>12 h): " + string.Join(", ", staleWarnings);
                log(msg);
                AppLogger.Warn(msg);
            }
        }

        private async Task TryFetchMeteologixApiAsync(
            string apiKey,
            ForecastResult result,
            WeatherParameterInfo paramInfo,
            DateTime startHour,
            DateTime endHour,
            Action<string> log,
            Action<int, int, string> progress,
            CancellationToken ct)
        {
            log("Dotazuji Meteologix / Kachelmannwetter API v02 (14 krajských bodů)...");
            var kachelmannModels = new[]
            {
                new { EndpointPath = "advanced/1h", Info = new WeatherModelInfo { Id = "meteologix_advanced", DisplayName = "Meteologix Advanced (API)", Provider = "Kachelmannwetter" } },
                new { EndpointPath = "3day", Info = new WeatherModelInfo { Id = "meteologix_3day", DisplayName = "Meteologix 3-Day (API)", Provider = "Kachelmannwetter" } }
            };

            try
            {
                foreach (var km in kachelmannModels)
                {
                    ct.ThrowIfCancellationRequested();
                    var sumsByRegion = new Dictionary<string, double?>();
                    int successCount = 0;
                    int forbiddenCount = 0;
                    int idx = 0;

                    foreach (var region in CzechData.Regions)
                    {
                        ct.ThrowIfCancellationRequested();
                        idx++;
                        if (progress != null)
                            progress(idx, CzechData.Regions.Count, "Stahuji " + km.Info.DisplayName + ": " + idx + " / 14 krajů...");

                        string endpoint = string.Format(
                            CultureInfo.InvariantCulture,
                            "https://api.kachelmannwetter.com/v02/forecast/{0:0.0000}/{1:0.0000}/{2}",
                            region.Latitude,
                            region.Longitude,
                            km.EndpointPath
                        );

                        string cacheKey = "mlx|" + endpoint;
                        string body = DiskCache.TryGet(cacheKey);

                        if (string.IsNullOrEmpty(body))
                        {
                            using (var req = new HttpRequestMessage(HttpMethod.Get, endpoint))
                            {
                                req.Headers.Add("X-API-Key", apiKey);
                                using (var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false))
                                {
                                    if (!resp.IsSuccessStatusCode)
                                    {
                                        if ((int)resp.StatusCode == 401)
                                        {
                                            log("Meteologix API vrátilo 401 Unauthorized. Zkontrolujte platnost X-API-Key klíče.");
                                            AppLogger.Warn("Meteologix API 401 Unauthorized.");
                                            return;
                                        }
                                        if ((int)resp.StatusCode == 403)
                                        {
                                            forbiddenCount++;
                                            sumsByRegion[region.Code] = null;
                                            continue;
                                        }
                                        sumsByRegion[region.Code] = null;
                                        continue;
                                    }

                                    body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(body))
                                        DiskCache.Put(cacheKey, body);
                                }
                            }
                        }

                        double? val = ParseKachelmannParameterValue(body, paramInfo, startHour, endHour);
                        sumsByRegion[region.Code] = val;
                        if (val.HasValue)
                            successCount++;
                    }

                    if (forbiddenCount > 0 && successCount == 0)
                    {
                        log(km.Info.DisplayName + ": " + forbiddenCount + "/14 krajů není povoleno v tarifu klíče (HTTP 403). Povolte lokality v accounts.meteologix.com.");
                    }

                    if (successCount > 0)
                    {
                        result.Models.Add(km.Info);
                        result.ModelNotes[km.Info.Id] = successCount < 14
                            ? "pouze " + successCount + "/14 povolených krajských bodů"
                            : "pouze 14 krajských bodů (bez sítě)";

                        foreach (var region in CzechData.Regions)
                        {
                            double? val;
                            sumsByRegion.TryGetValue(region.Code, out val);
                            result.RegionModelSums[region.Code][km.Info.Id] = val;
                        }
                        string note = forbiddenCount > 0 ? " (vynecháno " + forbiddenCount + " nepovolených krajů 403)" : "";
                        log(km.Info.DisplayName + " připojeno (" + successCount + "/14 krajů)" + note + ".");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log("Chyba při komunikaci s Meteologix API: " + ex.Message);
                AppLogger.Warn("Chyba Meteologix API: " + ex.Message);
            }
        }

        private double? ParseKachelmannParameterValue(string json, WeatherParameterInfo paramInfo, DateTime startHour, DateTime endHour)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("data"))
                return null;

            var dataArr = root["data"] as object[];
            if (dataArr == null)
                return null;

            double sum = 0;
            double max = double.MinValue;
            double min = double.MaxValue;
            int cnt = 0;

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
                        DateTime localDt = dt.ToLocalTime();
                        if (localDt < startHour || localDt > endHour)
                            continue;
                    }
                }

                double? val = ExtractKachelmannEntryField(entry, paramInfo.Parameter);
                if (val.HasValue)
                {
                    sum += val.Value;
                    if (val.Value > max) max = val.Value;
                    if (val.Value < min) min = val.Value;
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

        private static double? ExtractKachelmannEntryField(Dictionary<string, object> entry, WeatherParameter param)
        {
            switch (param)
            {
                case WeatherParameter.TotalPrecipitation:
                    return TryReadAny(entry, "prec", "precipitation", "precip");
                case WeatherParameter.Snowfall:
                    return TryReadAny(entry, "snow", "snowfall");
                case WeatherParameter.Temperature2mMax:
                case WeatherParameter.Temperature2mMin:
                    return TryReadAny(entry, "temp", "temperature", "tempMax", "tempMin");
                case WeatherParameter.DewPoint2m:
                    return TryReadAny(entry, "dewPoint", "dewpoint");
                case WeatherParameter.CloudCover:
                    return TryReadAny(entry, "cloudCover", "clouds", "cloudiness");
                case WeatherParameter.WindSpeed10m:
                    return TryReadAny(entry, "windSpeed", "wind");
                case WeatherParameter.WindGusts10m:
                    return TryReadAny(entry, "windGust", "windGusts", "gust");
                default:
                    return null;
            }
        }

        private static double? TryReadAny(Dictionary<string, object> dict, params string[] keys)
        {
            foreach (string k in keys)
            {
                if (dict.ContainsKey(k) && dict[k] != null)
                    return Convert.ToDouble(dict[k], CultureInfo.InvariantCulture);
            }
            return null;
        }
    }
}
