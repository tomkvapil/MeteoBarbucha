using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class ThermodiagramDialog : Form
    {
        private static readonly int[] Levels = { 1000, 975, 950, 925, 900, 850, 800, 700, 600, 500, 400, 300, 200 };
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(22) };

        private readonly double _lat;
        private readonly double _lon;
        private readonly string _regionCode;
        private readonly string _modelName;
        private readonly string _apiModelId;
        private readonly string _proxySourceNote;
        private readonly int _forecastDays;

        private readonly List<string> _times = new List<string>();
        private readonly Dictionary<int, double?[]> _tempByLevel = new Dictionary<int, double?[]>();
        private readonly Dictionary<int, double?[]> _rhByLevel = new Dictionary<int, double?[]>();
        private readonly Dictionary<int, double?[]> _windByLevel = new Dictionary<int, double?[]>();
        private double?[] _capeSeries;
        private double?[] _freezingSeries;

        private readonly Panel _canvas;
        private readonly TrackBar _trkTime;
        private readonly Label _lblTime;
        private readonly CheckBox _chkEmagram;
        private string _statusText = "Stahuji rozšířený vertikální profil (13 hladin + indexy)...";

        public ThermodiagramDialog(ForecastResult result, GeoGridPoint point, string regionCode, string modelName, string modelKey)
        {
            _lat = point.Latitude;
            _lon = point.Longitude;
            _regionCode = regionCode;
            _modelName = modelName;
            _forecastDays = Math.Max(1, Math.Min(3, result.ForecastDays));

            // Clearly document when a proxy upper-air model is used for surface-only models!
            if (modelKey == "chmi_aladin_cz_1km")
            {
                _apiModelId = "chmi_aladin_central_europe_2km";
                _proxySourceNote = "Zdroj výškových hladin: ALADIN Central Europe 2 km";
            }
            else if (modelKey == "brightsky_dwd")
            {
                _apiModelId = "icon_eu";
                _proxySourceNote = "Zástupný výškový profil: DWD ICON-EU (Bright Sky neobsahuje tlakové hladiny)";
            }
            else if (modelKey == "metno_seamless")
            {
                _apiModelId = "ecmwf_ifs025";
                _proxySourceNote = "Zástupný výškový profil: ECMWF IFS 0.25° (MET Norway API je pouze přízemní)";
            }
            else if (modelKey != null && modelKey.StartsWith("meteologix_", StringComparison.OrdinalIgnoreCase))
            {
                _apiModelId = "ecmwf_ifs025";
                _proxySourceNote = "Zástupný výškový profil: ECMWF IFS 0.25°";
            }
            else
            {
                _apiModelId = modelKey;
                _proxySourceNote = null;
            }

            Text = string.Format(
                CultureInfo.InvariantCulture,
                "Předpovědní termodiagram — {0} | {1:0.00}° N, {2:0.00}° E ({3})",
                modelName, _lat, _lon, regionCode);
            Size = new Size(1060, 720);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;

            var topBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(12, 6, 12, 6) };
            var lblTitle = new Label { Text = "Čas sondáže:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), Location = new Point(12, 13), AutoSize = true };
            _trkTime = new TrackBar { Location = new Point(108, 6), Width = 380, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None, Enabled = false };
            _lblTime = new Label { Text = "Načítám...", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(37, 99, 235), Location = new Point(496, 13), AutoSize = true };
            _chkEmagram = new CheckBox
            {
                Text = "Emagram (rovné izotermy)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(680, 12),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            _chkEmagram.CheckedChanged += (s, e) => _canvas.Invalidate();

            _trkTime.ValueChanged += (s, e) =>
            {
                if (_trkTime.Value >= 0 && _trkTime.Value < _times.Count)
                    _lblTime.Text = _times[_trkTime.Value].Replace("T", " ");
                _canvas.Invalidate();
            };

            topBar.Controls.AddRange(new Control[] { lblTitle, _trkTime, _lblTime, _chkEmagram });

            _canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _canvas.Paint += OnCanvasPaint;
            _canvas.Resize += (s, e) => _canvas.Invalidate();

            Controls.Add(_canvas);
            Controls.Add(topBar);

            Shown += async (s, e) => await LoadSoundingAsync();
        }

        private async Task LoadSoundingAsync()
        {
            try
            {
                var vars = new List<string> { "cape", "freezing_level_height" };
                foreach (int p in Levels)
                {
                    vars.Add("temperature_" + p + "hPa");
                    vars.Add("relative_humidity_" + p + "hPa");
                    vars.Add("wind_speed_" + p + "hPa");
                }

                string query = string.Format(
                    CultureInfo.InvariantCulture,
                    "latitude={0:0.00}&longitude={1:0.00}&hourly={2}&models={3}&timezone=Europe%2FPrague&forecast_days={4}",
                    _lat, _lon, string.Join(",", vars), _apiModelId, _forecastDays);
                var saved = SettingsManager.Load();
                string url = WeatherService.BuildOpenMeteoForecastUrl(query, saved != null ? saved.OpenMeteoCustomKeyOrUrl : null);

                string json = DiskCache.TryGet(url);
                if (string.IsNullOrEmpty(json))
                {
                    try
                    {
                        json = await _http.GetStringAsync(url);
                        DiskCache.Put(url, json);
                    }
                    catch
                    {
                        DateTime staleAt;
                        json = DiskCache.TryGetStale(url, out staleAt);
                        if (string.IsNullOrEmpty(json))
                            throw;
                    }
                }

                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("hourly"))
                    throw new Exception("Model neposkytuje výškové hladiny.");

                var hourly = root["hourly"] as Dictionary<string, object>;
                var timeArr = hourly["time"] as object[];
                if (timeArr == null || timeArr.Length == 0)
                    throw new Exception("Chybí časová osa sondáže.");

                _times.Clear();
                foreach (var t in timeArr)
                    _times.Add(Convert.ToString(t, CultureInfo.InvariantCulture));

                _capeSeries = ParseArray(hourly, "cape", _apiModelId);
                _freezingSeries = ParseArray(hourly, "freezing_level_height", _apiModelId);

                foreach (int p in Levels)
                {
                    _tempByLevel[p] = ParseArray(hourly, "temperature_" + p + "hPa", _apiModelId);
                    _rhByLevel[p] = ParseArray(hourly, "relative_humidity_" + p + "hPa", _apiModelId);
                    _windByLevel[p] = ParseArray(hourly, "wind_speed_" + p + "hPa", _apiModelId);
                }

                _statusText = null;
                _trkTime.Maximum = Math.Max(0, _times.Count - 1);
                _trkTime.Value = Math.Min(12, _trkTime.Maximum);
                _trkTime.Enabled = _times.Count > 1;
                _lblTime.Text = _times[_trkTime.Value].Replace("T", " ");
                _canvas.Invalidate();
            }
            catch (Exception ex)
            {
                _statusText = "Termodiagram nelze načíst: " + ex.Message;
                _lblTime.Text = "Nedostupné";
                _canvas.Invalidate();
            }
        }

        private static double?[] ParseArray(Dictionary<string, object> hourly, string varName, string modelId)
        {
            string key = varName + "_" + modelId;
            if (!hourly.ContainsKey(key) && hourly.ContainsKey(varName))
                key = varName;
            if (!hourly.ContainsKey(key))
                return null;

            var raw = hourly[key] as object[];
            if (raw == null) return null;
            var arr = new double?[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != null)
                    arr[i] = Convert.ToDouble(raw[i], CultureInfo.InvariantCulture);
            }
            return arr;
        }

        private static double ComputeDewPoint(double tC, double rh)
        {
            double clampedRh = Math.Max(1.0, Math.Min(100.0, rh));
            const double a = 17.625;
            const double b = 243.04;
            double alpha = Math.Log(clampedRh / 100.0) + (a * tC) / (b + tC);
            return (b * alpha) / (a - alpha);
        }

        private double? GetLevelTemp(int p, int ti)
        {
            double?[] arr;
            if (_tempByLevel.TryGetValue(p, out arr) && arr != null && ti < arr.Length)
                return arr[ti];
            return null;
        }

        private double? GetLevelDewPoint(int p, int ti)
        {
            double? t = GetLevelTemp(p, ti);
            double?[] rhArr;
            if (t.HasValue && _rhByLevel.TryGetValue(p, out rhArr) && rhArr != null && ti < rhArr.Length && rhArr[ti].HasValue)
                return ComputeDewPoint(t.Value, rhArr[ti].Value);
            return null;
        }

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = _canvas.Width;
            int h = _canvas.Height;

            using (var titleFont = new Font("Segoe UI", 11.2f, FontStyle.Bold))
            using (var subNoteFont = new Font("Segoe UI", 8.8f, FontStyle.Bold))
            using (var axisFont = new Font("Segoe UI", 8.2f))
            using (var boxHeadFont = new Font("Segoe UI", 9.2f, FontStyle.Bold))
            using (var darkBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            using (var warnBrush = new SolidBrush(Color.FromArgb(180, 83, 9)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
            {
                bool isEmagram = _chkEmagram.Checked;
                string diagType = isEmagram ? "Emagram" : "Skew-T / Log-P";
                string header = string.Format(
                    CultureInfo.InvariantCulture,
                    "Předpovědní termodiagram ({4}) — {0}  |  {1:0.00}° N, {2:0.00}° E ({3})",
                    _modelName, _lat, _lon, _regionCode, diagType);
                g.DrawString(header, titleFont, darkBrush, 20, 8);

                int topMargin = 42;
                if (!string.IsNullOrEmpty(_proxySourceNote))
                {
                    g.DrawString("ℹ " + _proxySourceNote, subNoteFont, warnBrush, 22, 30);
                    topMargin = 54;
                }

                if (!string.IsNullOrEmpty(_statusText) || _times.Count == 0)
                {
                    g.DrawString(_statusText ?? "Načítám...", titleFont, mutedBrush, 24, topMargin + 12);
                    return;
                }

                int rightPanelWidth = 285;
                Rectangle plot = new Rectangle(64, topMargin, w - 64 - rightPanelWidth - 16, h - topMargin - 50);
                if (plot.Width < 140 || plot.Height < 140) return;

                double logPMax = Math.Log10(1000.0);
                double logPMin = Math.Log10(200.0);
                float SkewFactor = isEmagram ? 0.0f : (0.55f * plot.Width / plot.Height);
                double minTempAxis = isEmagram ? -70.0 : -50.0;
                double maxTempAxis = 35.0;

                Func<double, float> pToY = p =>
                {
                    double frac = (logPMax - Math.Log10(p)) / (logPMax - logPMin);
                    return plot.Bottom - (float)(frac * plot.Height);
                };

                Func<double, float, float> tToX = (tempC, py) =>
                {
                    double baseFrac = (tempC - minTempAxis) / (maxTempAxis - minTempAxis);
                    float skewOffset = (plot.Bottom - py) * SkewFactor;
                    return plot.Left + (float)(baseFrac * plot.Width) + skewOffset;
                };

                Region prevClip = g.Clip;
                g.SetClip(plot);

                // Dry adiabats background lines
                using (var adiabatPen = new Pen(Color.FromArgb(241, 245, 249), 1f) { DashStyle = DashStyle.Dot })
                {
                    for (int thetaC = -30; thetaC <= 80; thetaC += 15)
                    {
                        double thetaK = thetaC + 273.15;
                        var adPts = new List<PointF>();
                        for (int p = 1000; p >= 200; p -= 50)
                        {
                            double tK = thetaK * Math.Pow(p / 1000.0, 0.286);
                            float py = pToY(p);
                            adPts.Add(new PointF(tToX(tK - 273.15, py), py));
                        }
                        if (adPts.Count >= 2)
                            g.DrawLines(adiabatPen, adPts.ToArray());
                    }
                }

                // Skewed / straight isotherms
                using (var isoPen = new Pen(Color.FromArgb(226, 232, 240), 1f))
                using (var zeroPen = new Pen(Color.FromArgb(59, 130, 246), 1.6f) { DashStyle = DashStyle.Dash })
                {
                    for (int t = -80; t <= 40; t += 10)
                    {
                        float xBot = tToX(t, plot.Bottom);
                        float xTop = tToX(t, plot.Top);
                        g.DrawLine(t == 0 ? zeroPen : isoPen, xBot, plot.Bottom, xTop, plot.Top);
                    }
                }

                // Horizontal isobars
                using (var barPen = new Pen(Color.FromArgb(203, 213, 225), 1f))
                {
                    foreach (int p in Levels)
                    {
                        float py = pToY(p);
                        g.DrawLine(barPen, plot.Left, py, plot.Right, py);
                    }
                }

                int ti = Math.Max(0, Math.Min(_times.Count - 1, _trkTime.Value));
                var tPts = new List<PointF>();
                var tdPts = new List<PointF>();

                foreach (int p in Levels)
                {
                    double? temp = GetLevelTemp(p, ti);
                    if (!temp.HasValue) continue;

                    float py = pToY(p);
                    tPts.Add(new PointF(tToX(temp.Value, py), py));

                    double? td = GetLevelDewPoint(p, ti);
                    if (td.HasValue)
                        tdPts.Add(new PointF(tToX(td.Value, py), py));
                }

                if (tdPts.Count >= 2)
                {
                    using (var tdPen = new Pen(Color.FromArgb(16, 185, 129), 2.5f))
                        g.DrawLines(tdPen, tdPts.ToArray());
                }
                if (tPts.Count >= 2)
                {
                    using (var tPen = new Pen(Color.FromArgb(220, 38, 38), 2.5f))
                        g.DrawLines(tPen, tPts.ToArray());
                }

                g.Clip = prevClip;

                using (var borderPen = new Pen(Color.FromArgb(100, 116, 139), 1.2f))
                    g.DrawRectangle(borderPen, plot);

                // Pressure labels + wind column
                int[] labeledLevels = { 1000, 950, 900, 850, 800, 700, 600, 500, 400, 300, 200 };
                var labelSet = new HashSet<int>(labeledLevels);
                g.DrawString("Vítr", axisFont, darkBrush, plot.Right + 8, plot.Top - 16);

                foreach (int p in Levels)
                {
                    if (!labelSet.Contains(p)) continue;
                    float py = pToY(p);
                    string pLbl = p + " hPa";
                    SizeF sz = g.MeasureString(pLbl, axisFont);
                    g.DrawString(pLbl, axisFont, mutedBrush, plot.Left - sz.Width - 6, py - sz.Height / 2f);

                    double?[] wArr;
                    if (_windByLevel.TryGetValue(p, out wArr) && wArr != null && ti < wArr.Length && wArr[ti].HasValue)
                    {
                        g.DrawString(wArr[ti].Value.ToString("0", CultureInfo.InvariantCulture) + " km/h", axisFont, darkBrush, plot.Right + 8, py - sz.Height / 2f);
                    }
                }

                // Bottom temperature labels
                for (int t = -70; t <= 30; t += 10)
                {
                    float xBot = tToX(t, plot.Bottom);
                    if (xBot >= plot.Left && xBot <= plot.Right)
                        g.DrawString(t + "°C", axisFont, mutedBrush, xBot - 12, plot.Bottom + 4);
                }

                // Legend under diagram
                using (var redB = new SolidBrush(Color.FromArgb(220, 38, 38)))
                using (var greenB = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    int ly = plot.Bottom + 24;
                    g.FillRectangle(redB, plot.Left, ly + 5, 16, 4);
                    g.DrawString("Teplota T (°C)", axisFont, darkBrush, plot.Left + 22, ly);
                    g.FillRectangle(greenB, plot.Left + 135, ly + 5, 16, 4);
                    g.DrawString("Rosný bod Td (°C)   |   Modrá čárkovaná: izoterma 0 °C", axisFont, darkBrush, plot.Left + 157, ly);
                }

                // Right-side Meteorological Indices Panel (LCL, K-Index, TT, LI, CAPE, 0°C level)
                Rectangle idxRect = new Rectangle(plot.Right + 68, plot.Top, rightPanelWidth - 68, plot.Height);
                DrawIndicesPanel(g, idxRect, ti, boxHeadFont, axisFont, darkBrush, mutedBrush);
            }
        }

        private void DrawIndicesPanel(Graphics g, Rectangle rect, int ti, Font headFont, Font bodyFont, Brush darkBrush, Brush mutedBrush)
        {
            if (rect.Width < 120) return;

            using (var panelBg = new SolidBrush(Color.FromArgb(248, 250, 252)))
            using (var panelBorder = new Pen(Color.FromArgb(203, 213, 225), 1f))
            {
                g.FillRectangle(panelBg, rect);
                g.DrawRectangle(panelBorder, rect);
            }

            int x = rect.Left + 10;
            int y = rect.Top + 10;
            g.DrawString("Indexy a parametry sondáže", headFont, darkBrush, x, y);
            y += 26;

            // Surface/low-level parcel (prefer 975 or 1000 hPa)
            int baseP = GetLevelTemp(975, ti).HasValue ? 975 : 1000;
            double? tLow = GetLevelTemp(baseP, ti);
            double? tdLow = GetLevelDewPoint(baseP, ti);

            double? t850 = GetLevelTemp(850, ti);
            double? td850 = GetLevelDewPoint(850, ti);
            double? t700 = GetLevelTemp(700, ti);
            double? td700 = GetLevelDewPoint(700, ti);
            double? t500 = GetLevelTemp(500, ti);

            // 1. LCL (Espy formula: h_AGL = 125 * (T - Td))
            string lclStr = "N/A";
            double? pLcl = null;
            double? tLcl = null;
            if (tLow.HasValue && tdLow.HasValue)
            {
                double spread = Math.Max(0.0, tLow.Value - tdLow.Value);
                double lclMeters = Math.Round(125.0 * spread);
                tLcl = tLow.Value - 0.0098 * lclMeters;
                pLcl = baseP * Math.Pow((tLcl.Value + 273.15) / (tLow.Value + 273.15), 3.5);
                lclStr = string.Format(CultureInfo.InvariantCulture, "~{0:0} m ({1:0} hPa)", lclMeters, pLcl.Value);
            }
            DrawIndexRow(g, x, ref y, "Hladina LCL:", lclStr, "Výška kondenzace", headFont, bodyFont, darkBrush, mutedBrush);

            // 2. K-Index = (T850 - T500) + Td850 - (T700 - Td700)
            string kiStr = "N/A";
            string kiDesc = "";
            if (t850.HasValue && t500.HasValue && td850.HasValue && t700.HasValue && td700.HasValue)
            {
                double ki = (t850.Value - t500.Value) + td850.Value - (t700.Value - td700.Value);
                kiStr = ki.ToString("0.0", CultureInfo.InvariantCulture) + " °C";
                if (ki < 20) kiDesc = "Nízké riziko bouřek";
                else if (ki < 30) kiDesc = "Ojedinělé bouřky (20–40 %)";
                else if (ki < 35) kiDesc = "Četné bouřky (60 %)";
                else kiDesc = "Velmi silné bouřky (>80 %)";
            }
            DrawIndexRow(g, x, ref y, "K-Index:", kiStr, kiDesc, headFont, bodyFont, darkBrush, mutedBrush);

            // 3. Total Totals = (T850 - T500) + (Td850 - T500)
            string ttStr = "N/A";
            string ttDesc = "";
            if (t850.HasValue && td850.HasValue && t500.HasValue)
            {
                double tt = (t850.Value - t500.Value) + (td850.Value - t500.Value);
                ttStr = tt.ToString("0.0", CultureInfo.InvariantCulture);
                if (tt < 44) ttDesc = "Stabilní zvrstvení";
                else if (tt < 50) ttDesc = "Možné bouřky";
                else ttDesc = "Riziko silných bouřek";
            }
            DrawIndexRow(g, x, ref y, "Total Totals (TT):", ttStr, ttDesc, headFont, bodyFont, darkBrush, mutedBrush);

            // 4. Lifted Index (LI) = T500_env - T500_parcel
            string liStr = "N/A";
            string liDesc = "";
            if (t500.HasValue && pLcl.HasValue && tLcl.HasValue)
            {
                double tParcel500;
                if (pLcl.Value <= 500.0)
                {
                    tParcel500 = (tLow.Value + 273.15) * Math.Pow(500.0 / baseP, 0.286) - 273.15;
                }
                else
                {
                    // Moist adiabatic ascent from pLcl to 500 hPa (~5.5 K/km average moist lapse rate)
                    double dzKm = 7.2 * Math.Log(pLcl.Value / 500.0);
                    double gammaMoist = tLcl.Value > 10.0 ? 4.8 : (tLcl.Value > 0.0 ? 5.6 : 6.8);
                    tParcel500 = tLcl.Value - gammaMoist * dzKm;
                }
                double li = t500.Value - tParcel500;
                liStr = li.ToString("0.0", CultureInfo.InvariantCulture) + " K";
                if (li > 2) liDesc = "Stabilní atmosféra";
                else if (li > 0) liDesc = "Slabá instabilita";
                else if (li > -4) liDesc = "Mírná instabilita (bouřky)";
                else liDesc = "Silná instabilita (LI ≤ −4)";
            }
            DrawIndexRow(g, x, ref y, "Lifted Index (LI):", liStr, liDesc, headFont, bodyFont, darkBrush, mutedBrush);

            // 5. CAPE
            string capeStr = (_capeSeries != null && ti < _capeSeries.Length && _capeSeries[ti].HasValue)
                ? _capeSeries[ti].Value.ToString("0", CultureInfo.InvariantCulture) + " J/kg"
                : "N/A";
            DrawIndexRow(g, x, ref y, "CAPE (model):", capeStr, "Konvekční dostupná energie", headFont, bodyFont, darkBrush, mutedBrush);

            // 6. Freezing level height
            string frzStr = (_freezingSeries != null && ti < _freezingSeries.Length && _freezingSeries[ti].HasValue)
                ? _freezingSeries[ti].Value.ToString("0", CultureInfo.InvariantCulture) + " m n. m."
                : "N/A";
            DrawIndexRow(g, x, ref y, "Hladina 0 °C:", frzStr, "Výška nulové izotermy", headFont, bodyFont, darkBrush, mutedBrush);

            // 7. Key level temperatures summary
            y += 6;
            string tSum = string.Format(
                CultureInfo.InvariantCulture,
                "T850: {0}  |  T500: {1}",
                t850.HasValue ? (t850.Value.ToString("0.0", CultureInfo.InvariantCulture) + " °C") : "—",
                t500.HasValue ? (t500.Value.ToString("0.0", CultureInfo.InvariantCulture) + " °C") : "—");
            g.DrawString(tSum, bodyFont, darkBrush, x, y);
        }

        private static void DrawIndexRow(
            Graphics g, int x, ref int y,
            string label, string val, string desc,
            Font headFont, Font bodyFont, Brush darkBrush, Brush mutedBrush)
        {
            g.DrawString(label, bodyFont, mutedBrush, x, y);
            g.DrawString(val, headFont, darkBrush, x + 102, y - 1);
            y += 18;
            if (!string.IsNullOrEmpty(desc))
            {
                g.DrawString(desc, bodyFont, mutedBrush, x + 4, y);
                y += 20;
            }
            else
            {
                y += 8;
            }
        }
    }
}
