using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class MeteogramDialog : Form
    {
        private readonly ForecastResult _result;
        private readonly GeoGridPoint _point;
        private readonly string _regionCode;

        private static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(37, 99, 235),
            Color.FromArgb(220, 38, 38),
            Color.FromArgb(16, 185, 129),
            Color.FromArgb(245, 158, 11),
            Color.FromArgb(139, 92, 246),
            Color.FromArgb(236, 72, 153),
            Color.FromArgb(20, 184, 166),
            Color.FromArgb(249, 115, 22),
            Color.FromArgb(71, 85, 105),
            Color.FromArgb(132, 204, 22),
            Color.FromArgb(14, 116, 144),
            Color.FromArgb(190, 24, 93)
        };

        public MeteogramDialog(ForecastResult result, GeoGridPoint point, string regionCode)
        {
            _result = result;
            _point = point;
            _regionCode = regionCode;

            var paramInfo = CzechData.GetParameterInfo(result.Parameter);
            Text = string.Format(
                CultureInfo.InvariantCulture,
                "Meteogram a rozptyl P10–P90 — {0} ({1:0.00}° N, {2:0.00}° E, {3})",
                paramInfo.DisplayName, point.Latitude, point.Longitude, regionCode);
            Size = new Size(960, 600);
            MinimumSize = new Size(720, 440);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;

            var canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            canvas.Paint += OnCanvasPaint;
            canvas.Resize += (s, e) => canvas.Invalidate();
            Controls.Add(canvas);
        }

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var ctrl = (Control)sender;
            int w = ctrl.Width;
            int h = ctrl.Height;

            var paramInfo = CzechData.GetParameterInfo(_result.Parameter);
            string title = string.Format(
                CultureInfo.InvariantCulture,
                "{0}  |  Bod: {1:0.00}° N, {2:0.00}° E ({3})  |  Pásmo P10–P90 a medián",
                paramInfo.DisplayName, _point.Latitude, _point.Longitude, _regionCode);

            using (var titleFont = new Font("Segoe UI", 11.5f, FontStyle.Bold))
            using (var axisFont = new Font("Segoe UI", 8.2f))
            using (var legFont = new Font("Segoe UI", 8.3f, FontStyle.Bold))
            using (var darkBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
            using (var gridPen = new Pen(Color.FromArgb(226, 232, 240), 1f))
            {
                g.DrawString(title, titleFont, darkBrush, 24, 14);

                int left = 64, top = 52, right = w - 24, bottom = h - 122;
                if (right <= left + 40 || bottom <= top + 40) return;

                double minVal = double.MaxValue;
                double maxVal = double.MinValue;
                int maxLen = 0;

                foreach (var m in _result.Models)
                {
                    double?[] series;
                    if (_point.ModelSeries.TryGetValue(m.Id, out series) && series != null)
                    {
                        if (series.Length > maxLen) maxLen = series.Length;
                        foreach (var v in series)
                        {
                            if (v.HasValue)
                            {
                                if (v.Value < minVal) minVal = v.Value;
                                if (v.Value > maxVal) maxVal = v.Value;
                            }
                        }
                    }
                }

                if (maxLen < 2 || minVal == double.MaxValue)
                {
                    g.DrawString("Časová řada není pro tento bod k dispozici.", titleFont, mutedBrush, left, top + 20);
                    return;
                }

                if (Math.Abs(maxVal - minVal) < 0.5)
                {
                    minVal = Math.Floor(minVal - 1);
                    maxVal = Math.Ceiling(maxVal + 1);
                }
                else
                {
                    double pad = (maxVal - minVal) * 0.1;
                    maxVal += pad;
                    if (minVal >= 0 && minVal - pad < 0) minVal = 0;
                    else minVal -= pad;
                }

                Rectangle plot = new Rectangle(left, top, right - left, bottom - top);
                g.DrawRectangle(gridPen, plot);

                int yTicks = 5;
                for (int i = 0; i <= yTicks; i++)
                {
                    double frac = (double)i / yTicks;
                    float py = plot.Bottom - (float)(frac * plot.Height);
                    double val = minVal + frac * (maxVal - minVal);
                    g.DrawLine(gridPen, plot.Left, py, plot.Right, py);
                    string lbl = val.ToString("0.0", CultureInfo.InvariantCulture);
                    SizeF sz = g.MeasureString(lbl, axisFont);
                    g.DrawString(lbl, axisFont, mutedBrush, plot.Left - sz.Width - 6, py - sz.Height / 2f);
                }

                // X-axis labels
                int xStep = Math.Max(1, maxLen / 8);
                for (int i = 0; i < maxLen; i += xStep)
                {
                    float px = plot.Left + (float)i / (maxLen - 1) * plot.Width;
                    g.DrawLine(gridPen, px, plot.Top, px, plot.Bottom);
                    if (i < _result.TimeSteps.Count)
                    {
                        string tLbl = _result.TimeSteps[i].Replace("T", " ");
                        if (tLbl.Length > 10 && tLbl.Contains(" ")) tLbl = tLbl.Substring(5);
                        g.DrawString(tLbl, axisFont, mutedBrush, px - 24, plot.Bottom + 4);
                    }
                }

                // Draw P10–P90 shaded band + Median curve when >= 3 models have series
                var p10Pts = new List<PointF>();
                var p90Pts = new List<PointF>();
                var medPts = new List<PointF>();
                var stepVals = new List<double>(_result.Models.Count);

                for (int i = 0; i < maxLen; i++)
                {
                    stepVals.Clear();
                    foreach (var m in _result.Models)
                    {
                        double?[] ser;
                        if (_point.ModelSeries.TryGetValue(m.Id, out ser) && ser != null && i < ser.Length && ser[i].HasValue)
                            stepVals.Add(ser[i].Value);
                    }
                    if (stepVals.Count >= 3)
                    {
                        float px = plot.Left + (float)i / (maxLen - 1) * plot.Width;
                        double p10 = EnsembleStats.ComputePercentile(stepVals, 10.0).Value;
                        double p50 = EnsembleStats.ComputePercentile(stepVals, 50.0).Value;
                        double p90 = EnsembleStats.ComputePercentile(stepVals, 90.0).Value;

                        float y10 = plot.Bottom - (float)((p10 - minVal) / (maxVal - minVal) * plot.Height);
                        float y50 = plot.Bottom - (float)((p50 - minVal) / (maxVal - minVal) * plot.Height);
                        float y90 = plot.Bottom - (float)((p90 - minVal) / (maxVal - minVal) * plot.Height);
                        p10Pts.Add(new PointF(px, y10));
                        p90Pts.Add(new PointF(px, y90));
                        medPts.Add(new PointF(px, y50));
                    }
                }

                if (p10Pts.Count >= 2)
                {
                    var bandPoly = new PointF[p10Pts.Count + p90Pts.Count];
                    for (int i = 0; i < p90Pts.Count; i++)
                        bandPoly[i] = p90Pts[i];
                    for (int i = 0; i < p10Pts.Count; i++)
                        bandPoly[p90Pts.Count + i] = p10Pts[p10Pts.Count - 1 - i];

                    using (var bandBrush = new SolidBrush(Color.FromArgb(48, 59, 130, 246)))
                    {
                        g.FillPolygon(bandBrush, bandPoly);
                    }
                }

                // Plot individual model lines
                for (int mi = 0; mi < _result.Models.Count; mi++)
                {
                    var m = _result.Models[mi];
                    double?[] series;
                    if (!_point.ModelSeries.TryGetValue(m.Id, out series) || series == null) continue;

                    Color col = Palette[mi % Palette.Length];
                    using (var pen = new Pen(col, 1.8f))
                    {
                        PointF? prev = null;
                        for (int i = 0; i < series.Length; i++)
                        {
                            if (!series[i].HasValue) { prev = null; continue; }
                            float px = plot.Left + (float)i / (maxLen - 1) * plot.Width;
                            double frac = (series[i].Value - minVal) / (maxVal - minVal);
                            float py = plot.Bottom - (float)(frac * plot.Height);
                            PointF cur = new PointF(px, py);
                            if (prev.HasValue)
                                g.DrawLine(pen, prev.Value, cur);
                            prev = cur;
                        }
                    }
                }

                // Plot bold black dashed Median curve on top
                if (medPts.Count >= 2)
                {
                    using (var medPen = new Pen(Color.FromArgb(15, 23, 42), 2.6f) { DashStyle = DashStyle.Dash })
                    {
                        g.DrawLines(medPen, medPts.ToArray());
                    }
                }

                // Warning threshold line if within axis
                double? th = _result.WarningThreshold;
                if (th.HasValue && th.Value >= minVal && th.Value <= maxVal)
                {
                    double frac = (th.Value - minVal) / (maxVal - minVal);
                    float wy = plot.Bottom - (float)(frac * plot.Height);
                    using (var warnPen = new Pen(Color.FromArgb(220, 38, 38), 1.8f) { DashStyle = DashStyle.DashDot })
                    {
                        g.DrawLine(warnPen, plot.Left, wy, plot.Right, wy);
                    }
                }

                // Legend at bottom
                int lx = left;
                int ly = bottom + 28;
                if (medPts.Count >= 2)
                {
                    string medLbl = "Medián (P50) + pásmo P10–P90";
                    SizeF msz = g.MeasureString(medLbl, legFont);
                    using (var bandB = new SolidBrush(Color.FromArgb(90, 59, 130, 246)))
                    using (var medP = new Pen(Color.FromArgb(15, 23, 42), 2f) { DashStyle = DashStyle.Dash })
                    {
                        g.FillRectangle(bandB, lx, ly + 2, 18, 12);
                        g.DrawLine(medP, lx, ly + 8, lx + 18, ly + 8);
                    }
                    g.DrawString(medLbl, legFont, darkBrush, lx + 22, ly);
                    lx += (int)msz.Width + 36;
                }

                for (int mi = 0; mi < _result.Models.Count; mi++)
                {
                    var m = _result.Models[mi];
                    bool derived = _result.ModelNotes != null && _result.ModelNotes.ContainsKey(m.Id) && !m.Id.StartsWith("meteologix_", StringComparison.OrdinalIgnoreCase);
                    string lbl = m.DisplayName + (derived ? " *" : "");
                    Color col = Palette[mi % Palette.Length];
                    SizeF sz = g.MeasureString(lbl, legFont);
                    if (lx + 24 + sz.Width > right)
                    {
                        lx = left;
                        ly += 20;
                    }
                    using (var b = new SolidBrush(col))
                    {
                        g.FillRectangle(b, lx, ly + 5, 14, 5);
                    }
                    g.DrawString(lbl, legFont, darkBrush, lx + 18, ly);
                    lx += (int)sz.Width + 28;
                }
            }
        }
    }
}
