using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
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
            Action<string> log)
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

            // Build batch coordinates for all 14 regions
            var latList = new List<string>();
            var lonList = new List<string>();
            foreach (var r in CzechData.Regions)
            {
                latList.Add(r.Latitude.ToString("0.0000", CultureInfo.InvariantCulture));
                lonList.Add(r.Longitude.ToString("0.0000", CultureInfo.InvariantCulture));
                result.RegionModelSums[r.Code] = new Dictionary<string, double?>();
            }

            var modelIds = new List<string>();
            foreach (var m in selectedModels)
                modelIds.Add(m.Id);

            string url;
            string sectionKey;
            string varPrefix;
            bool queryHourly = useHourly || paramInfo.ForceHourly;

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
                log("Stahuji hodinová data — " + paramInfo.DisplayName + " (" + result.PeriodDescription + ") pro 14 krajů a " + selectedModels.Count + " modelů...");
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
                log("Stahuji hodinová data — " + paramInfo.DisplayName + " (" + forecastDays + " dny) pro 14 krajů a " + selectedModels.Count + " modelů...");
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
                log("Stahuji denní data — " + paramInfo.DisplayName + " (" + forecastDays + " dny) pro 14 krajů a " + selectedModels.Count + " modelů...");
            }

            string json = await _httpClient.GetStringAsync(url);

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            object parsed = serializer.DeserializeObject(json);

            object[] regionItems = parsed as object[];
            if (regionItems == null)
                regionItems = new object[] { parsed };

            if (regionItems.Length != CzechData.Regions.Count)
            {
                log("Upozornění: Počet vrácených lokalit (" + regionItems.Length + ") neodpovídá počtu krajů (" + CzechData.Regions.Count + ").");
            }

            int count = Math.Min(regionItems.Length, CzechData.Regions.Count);
            for (int i = 0; i < count; i++)
            {
                var region = CzechData.Regions[i];
                var itemDict = regionItems[i] as Dictionary<string, object>;
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
                            result.Dates.Add(Convert.ToString(t, CultureInfo.InvariantCulture));
                    }
                }

                foreach (var model in selectedModels)
                {
                    result.RegionModelSums[region.Code][model.Id] = ExtractModelValue(
                        section, model.Id, selectedModels.Count == 1, paramInfo, varPrefix);
                }
            }

            if (!useHourly && result.Dates.Count > 0)
            {
                string firstDate = result.Dates[0];
                string lastDate = result.Dates[result.Dates.Count - 1];
                if (firstDate.Length > 10) firstDate = firstDate.Substring(0, 10);
                if (lastDate.Length > 10) lastDate = lastDate.Substring(0, 10);
                result.PeriodDescription = firstDate + " až " + lastDate + " (" + forecastDays + " dny)";
            }

            log("Open-Meteo data pro kraje úspěšně stažena.");

            // Fetch dense spatial grid across Czechia (~220 points) for accurate maps
            await FetchSpatialGridAsync(selectedModels, forecastDays, useHourly, paramInfo, cleanStart, cleanEnd, sectionKey, varPrefix, result, log);

            // Optional Meteologix / Kachelmannwetter API check if key is provided
            if (!string.IsNullOrWhiteSpace(meteologixApiKey))
            {
                await TryFetchMeteologixApiAsync(meteologixApiKey.Trim(), result, useHourly ? (DateTime?)cleanStart : null, useHourly ? (DateTime?)cleanEnd : null, log);
            }

            return result;
        }

        public static Dictionary<string, Dictionary<string, double?>> ComputeRegionalTable(ForecastResult result, RegionAggregationMode mode)
        {
            if (mode == RegionAggregationMode.PointValue || result.GridPoints == null || result.GridPoints.Count == 0)
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
                        double? v;
                        if (gp.ModelValues.TryGetValue(m.Id, out v) && v.HasValue)
                        {
                            sum += v.Value;
                            if (v.Value > max) max = v.Value;
                            cnt++;
                        }
                    }

                    if (cnt > 0)
                        table[r.Code][m.Id] = Math.Round(mode == RegionAggregationMode.RegionMax ? max : (sum / cnt), 2);
                    else
                    {
                        double? fallback;
                        result.RegionModelSums[r.Code].TryGetValue(m.Id, out fallback);
                        table[r.Code][m.Id] = fallback;
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

        private async Task FetchSpatialGridAsync(
            List<WeatherModelInfo> selectedModels,
            int forecastDays,
            bool useHourly,
            WeatherParameterInfo paramInfo,
            DateTime cleanStart,
            DateTime cleanEnd,
            string sectionKey,
            string varPrefix,
            ForecastResult result,
            Action<string> log)
        {
            var gridLats = new List<string>();
            var gridLons = new List<string>();
            var gridPts = new List<GeoGridPoint>();

            for (double lat = 48.55; lat <= 51.06; lat += 0.25)
            {
                for (double lon = 12.10; lon <= 18.85; lon += 0.35)
                {
                    double rLat = Math.Round(lat, 2);
                    double rLon = Math.Round(lon, 2);
                    gridLats.Add(rLat.ToString("0.00", CultureInfo.InvariantCulture));
                    gridLons.Add(rLon.ToString("0.00", CultureInfo.InvariantCulture));
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
            if (useHourly)
            {
                string sParam = cleanStart.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
                string eParam = cleanEnd.ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture);
                url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&hourly={2}&models={3}&timezone=Europe%2FPrague&start_hour={4}&end_hour={5}",
                    string.Join(",", gridLats),
                    string.Join(",", gridLons),
                    varPrefix,
                    string.Join(",", modelIds),
                    Uri.EscapeDataString(sParam),
                    Uri.EscapeDataString(eParam)
                );
            }
            else if (paramInfo.ForceHourly)
            {
                url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&hourly={2}&models={3}&timezone=Europe%2FPrague&forecast_days={4}",
                    string.Join(",", gridLats),
                    string.Join(",", gridLons),
                    varPrefix,
                    string.Join(",", modelIds),
                    forecastDays
                );
            }
            else
            {
                url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily={2}&models={3}&timezone=Europe%2FPrague&forecast_days={4}",
                    string.Join(",", gridLats),
                    string.Join(",", gridLons),
                    varPrefix,
                    string.Join(",", modelIds),
                    forecastDays
                );
            }

            log("Stahuji prostorovou síť (" + gridPts.Count + " bodů) pro přesné mapové snímky...");
            string json = await _httpClient.GetStringAsync(url);

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            object parsed = serializer.DeserializeObject(json);
            object[] items = parsed as object[] ?? new object[] { parsed };

            int count = Math.Min(items.Length, gridPts.Count);
            for (int i = 0; i < count; i++)
            {
                var itemDict = items[i] as Dictionary<string, object>;
                if (itemDict == null || !itemDict.ContainsKey(sectionKey))
                    continue;

                var section = itemDict[sectionKey] as Dictionary<string, object>;
                if (section == null)
                    continue;

                foreach (var model in selectedModels)
                {
                    gridPts[i].ModelValues[model.Id] = ExtractModelValue(
                        section, model.Id, selectedModels.Count == 1, paramInfo, varPrefix);
                }
            }

            result.GridPoints = gridPts;
            log("Prostorová síť (" + count + " bodů) načtena.");
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
