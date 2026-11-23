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
            Color.FromArgb(132, 204, 22)
        };

        public MeteogramDialog(ForecastResult result, GeoGridPoint point, string regionCode)
        {
            _result = result;
            _point = point;
            _regionCode = regionCode;

            var paramInfo = CzechData.GetParameterInfo(result.Parameter);
            Text = string.Format(
                CultureInfo.InvariantCulture,
                "Meteogram — {0} ({1:0.00}° N, {2:0.00}° E, {3})",
                paramInfo.DisplayName, point.Latitude, point.Longitude, regionCode);
            Size = new Size(920, 560);
            MinimumSize = new Size(700, 420);
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
                "{0}  |  Bod: {1:0.00}° N, {2:0.00}° E ({3})",
                paramInfo.DisplayName, _point.Latitude, _point.Longitude, _regionCode);

            using (var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var axisFont = new Font("Segoe UI", 8.2f))
            using (var legFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var darkBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
            using (var gridPen = new Pen(Color.FromArgb(226, 232, 240), 1f))
            {
                g.DrawString(title, titleFont, darkBrush, 24, 14);

                int left = 64, top = 54, right = w - 24, bottom = h - 105;
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
                    if (minVal > 0 && minVal - pad < 0) minVal = 0;
                    else minVal -= pad;
                }

                Rectangle plot = new Rectangle(left, top, right - left, bottom - top);
                g.DrawRectangle(gridPen, plot);

                // Horizontal grid lines
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

                // Warning threshold line if active
                if (_result.WarningThreshold.HasValue && _result.WarningThreshold.Value >= minVal && _result.WarningThreshold.Value <= maxVal)
                {
                    double frac = (_result.WarningThreshold.Value - minVal) / (maxVal - minVal);
                    float wy = plot.Bottom - (float)(frac * plot.Height);
                    using (var warnPen = new Pen(Color.FromArgb(220, 38, 38), 1.5f) { DashStyle = DashStyle.Dash })
                    {
                        g.DrawLine(warnPen, plot.Left, wy, plot.Right, wy);
                    }
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

                // Plot model lines
                for (int mi = 0; mi < _result.Models.Count; mi++)
                {
                    var m = _result.Models[mi];
                    double?[] series;
                    if (!_point.ModelSeries.TryGetValue(m.Id, out series) || series == null) continue;

                    Color col = Palette[mi % Palette.Length];
                    using (var pen = new Pen(col, 2.0f))
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

                // Legend at bottom
                int lx = left;
                int ly = bottom + 30;
                for (int mi = 0; mi < _result.Models.Count; mi++)
                {
                    var m = _result.Models[mi];
                    Color col = Palette[mi % Palette.Length];
                    SizeF sz = g.MeasureString(m.DisplayName, legFont);
                    if (lx + 24 + sz.Width > right)
                    {
                        lx = left;
                        ly += 20;
                    }
                    using (var b = new SolidBrush(col))
                    {
                        g.FillRectangle(b, lx, ly + 4, 14, 6);
                    }
                    g.DrawString(m.DisplayName, legFont, darkBrush, lx + 18, ly);
                    lx += (int)sz.Width + 32;
                }
            }
        }
    }
}
