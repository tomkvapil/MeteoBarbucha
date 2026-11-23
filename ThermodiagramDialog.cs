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
        private static readonly int[] Levels = { 1000, 925, 850, 700, 500, 300, 200 };
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private readonly double _lat;
        private readonly double _lon;
        private readonly string _regionCode;
        private readonly string _modelName;
        private readonly string _apiModelId;
        private readonly int _forecastDays;

        private readonly List<string> _times = new List<string>();
        private readonly Dictionary<int, double?[]> _tempByLevel = new Dictionary<int, double?[]>();
        private readonly Dictionary<int, double?[]> _rhByLevel = new Dictionary<int, double?[]>();
        private readonly Dictionary<int, double?[]> _windByLevel = new Dictionary<int, double?[]>();

        private readonly Panel _canvas;
        private readonly TrackBar _trkTime;
        private readonly Label _lblTime;
        private readonly CheckBox _chkEmagram;
        private string _statusText = "Stahuji vertikální profil (termodiagram)...";

        public ThermodiagramDialog(ForecastResult result, GeoGridPoint point, string regionCode, string modelName, string modelKey)
        {
            _lat = point.Latitude;
            _lon = point.Longitude;
            _regionCode = regionCode;
            _modelName = modelName;
            _forecastDays = Math.Max(1, Math.Min(3, result.ForecastDays));
            // ALADIN CZ 1km is surface-only; use ALADIN Central Europe 2km for vertical pressure levels
            _apiModelId = modelKey == "chmi_aladin_cz_1km" ? "chmi_aladin_central_europe_2km" : modelKey;

            Text = string.Format(
                CultureInfo.InvariantCulture,
                "Předpovědní termodiagram (Skew-T) — {0} | {1:0.00}° N, {2:0.00}° E ({3})",
                modelName, _lat, _lon, regionCode);
            Size = new Size(920, 680);
            MinimumSize = new Size(720, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;

            var topBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(12, 6, 12, 6) };
            var lblTitle = new Label { Text = "Čas sondáže:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), Location = new Point(12, 13), AutoSize = true };
            _trkTime = new TrackBar { Location = new Point(108, 6), Width = 360, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None, Enabled = false };
            _lblTime = new Label { Text = "Načítám...", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(37, 99, 235), Location = new Point(476, 13), AutoSize = true };
            _chkEmagram = new CheckBox
            {
                Text = "Emagram (rovné izotermy)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(660, 12),
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
                var vars = new List<string>();
                foreach (int p in Levels)
                {
                    vars.Add("temperature_" + p + "hPa");
                    vars.Add("relative_humidity_" + p + "hPa");
                    vars.Add("wind_speed_" + p + "hPa");
                }

                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0:0.00}&longitude={1:0.00}&hourly={2}&models={3}&timezone=Europe%2FPrague&forecast_days={4}",
                    _lat, _lon, string.Join(",", vars), _apiModelId, _forecastDays);

                string json = await _http.GetStringAsync(url);
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

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = _canvas.Width;
            int h = _canvas.Height;

            using (var titleFont = new Font("Segoe UI", 11.5f, FontStyle.Bold))
            using (var axisFont = new Font("Segoe UI", 8.5f))
            using (var darkBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
            {
                bool isEmagram = _chkEmagram.Checked;
                string diagType = isEmagram ? "Emagram" : "Skew-T / Log-P";
                string header = string.Format(
                    CultureInfo.InvariantCulture,
                    "Předpovědní termodiagram ({4}) — {0}  |  {1:0.00}° N, {2:0.00}° E ({3})",
                    _modelName, _lat, _lon, _regionCode, diagType);
                g.DrawString(header, titleFont, darkBrush, 20, 10);

                if (!string.IsNullOrEmpty(_statusText) || _times.Count == 0)
                {
                    g.DrawString(_statusText ?? "Načítám...", titleFont, mutedBrush, 24, 52);
                    return;
                }

                Rectangle plot = new Rectangle(64, 44, w - 165, h - 90);
                if (plot.Width < 120 || plot.Height < 120) return;

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

                // Skewed isotherms (-70 to +40 C)
                using (var isoPen = new Pen(Color.FromArgb(226, 232, 240), 1f))
                using (var zeroPen = new Pen(Color.FromArgb(59, 130, 246), 1.5f) { DashStyle = DashStyle.Dash })
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
                    double?[] tArr, rhArr;
                    _tempByLevel.TryGetValue(p, out tArr);
                    _rhByLevel.TryGetValue(p, out rhArr);
                    if (tArr == null || ti >= tArr.Length || !tArr[ti].HasValue) continue;

                    float py = pToY(p);
                    double temp = tArr[ti].Value;
                    tPts.Add(new PointF(tToX(temp, py), py));

                    if (rhArr != null && ti < rhArr.Length && rhArr[ti].HasValue)
                    {
                        double td = ComputeDewPoint(temp, rhArr[ti].Value);
                        tdPts.Add(new PointF(tToX(td, py), py));
                    }
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

                // Pressure labels + right-side wind column
                g.DrawString("Vítr (km/h)", axisFont, darkBrush, plot.Right + 12, plot.Top - 18);
                foreach (int p in Levels)
                {
                    float py = pToY(p);
                    string pLbl = p + " hPa";
                    SizeF sz = g.MeasureString(pLbl, axisFont);
                    g.DrawString(pLbl, axisFont, mutedBrush, plot.Left - sz.Width - 6, py - sz.Height / 2f);

                    double?[] wArr;
                    if (_windByLevel.TryGetValue(p, out wArr) && wArr != null && ti < wArr.Length && wArr[ti].HasValue)
                    {
                        g.DrawString(wArr[ti].Value.ToString("0", CultureInfo.InvariantCulture) + " km/h", axisFont, darkBrush, plot.Right + 14, py - sz.Height / 2f);
                    }
                }

                // Bottom temperature labels
                for (int t = -70; t <= 30; t += 10)
                {
                    float xBot = tToX(t, plot.Bottom);
                    if (xBot >= plot.Left && xBot <= plot.Right)
                        g.DrawString(t + "°C", axisFont, mutedBrush, xBot - 12, plot.Bottom + 4);
                }

                // Legend
                using (var redB = new SolidBrush(Color.FromArgb(220, 38, 38)))
                using (var greenB = new SolidBrush(Color.FromArgb(16, 185, 129)))
                using (var blueB = new SolidBrush(Color.FromArgb(59, 130, 246)))
                {
                    int ly = plot.Bottom + 26;
                    g.FillRectangle(redB, plot.Left, ly + 4, 16, 4);
                    g.DrawString("Teplota T (°C)", axisFont, darkBrush, plot.Left + 22, ly);
                    g.FillRectangle(greenB, plot.Left + 140, ly + 4, 16, 4);
                    g.DrawString("Rosný bod Td (°C)", axisFont, darkBrush, plot.Left + 162, ly);
                    g.FillRectangle(blueB, plot.Left + 300, ly + 4, 16, 4);
                    g.DrawString("Izoterma 0 °C (čárkovaně)", axisFont, darkBrush, plot.Left + 322, ly);
                }
            }
        }
    }
}
