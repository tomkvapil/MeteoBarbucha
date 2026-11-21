using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace MeteoSrazky
{
    public static class MapRenderer
    {
        private const double MinLon = 12.00;
        private const double MaxLon = 18.95;
        private const double MinLat = 48.50;
        private const double MaxLat = 51.12;

        public static string SaveModelMapSnapshot(
            string outputFolder,
            string modelKey,
            string modelDisplayName,
            ForecastResult result,
            Dictionary<string, double?> valuesByRegion)
        {
            Directory.CreateDirectory(outputFolder);
            string safeName = SanitizeFileName(modelKey) + ".png";
            string filePath = Path.Combine(outputFolder, safeName);

            using (var bmp = RenderMapBitmap(modelKey, modelDisplayName, result, valuesByRegion))
            {
                bmp.Save(filePath, ImageFormat.Png);
            }

            return filePath;
        }

        public static Bitmap RenderMapBitmap(
            string modelDisplayName,
            ForecastResult result,
            Dictionary<string, double?> valuesByRegion)
        {
            return RenderMapBitmap(null, modelDisplayName, result, valuesByRegion);
        }

        private const int MapWidth = 1200;
        private const int MapHeight = 760;

        public static bool TryImagePixelToLatLon(PointF imgPt, out double lat, out double lon)
        {
            Rectangle mapRect = new Rectangle(30, 96, MapWidth - 60, MapHeight - 185);
            Rectangle projRect = FitProjectionRect(mapRect, 24);
            if (!projRect.Contains((int)imgPt.X, (int)imgPt.Y))
            {
                lat = 0;
                lon = 0;
                return false;
            }
            lon = MinLon + ((imgPt.X - projRect.Left) / projRect.Width) * (MaxLon - MinLon);
            lat = MaxLat - ((imgPt.Y - projRect.Top) / projRect.Height) * (MaxLat - MinLat);
            return true;
        }

        public static Bitmap RenderMapBitmap(
            string modelKey,
            string modelDisplayName,
            ForecastResult result,
            Dictionary<string, double?> valuesByRegion)
        {
            int width = MapWidth;
            int height = MapHeight;
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var paramInfo = CzechData.GetParameterInfo(result.Parameter);
            string unit = string.IsNullOrEmpty(result.Unit) ? paramInfo.Unit : result.Unit;
            string paramTitle = paramInfo.MapTitle;

            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(245, 248, 252));

                // Top header bar
                using (var headerBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                {
                    g.FillRectangle(headerBrush, 0, 0, width, 78);
                }

                using (var titleFont = new Font("Segoe UI", 19f, FontStyle.Bold))
                using (var subFont = new Font("Segoe UI", 10.5f, FontStyle.Regular))
                using (var whiteBrush = new SolidBrush(Color.White))
                using (var mutedBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
                {
                    g.DrawString(paramTitle + " — " + modelDisplayName, titleFont, whiteBrush, 24, 14);
                    string dateSpan = !string.IsNullOrEmpty(result.PeriodDescription)
                        ? result.PeriodDescription
                        : (result.Dates.Count > 0
                            ? result.Dates[0] + " až " + result.Dates[result.Dates.Count - 1] + " (" + result.ForecastDays + " dny)"
                            : result.ForecastDays + " dní");
                    string subtitle = "Období: " + dateSpan + "   |   Vygenerováno: " + result.FetchedAt.ToString("dd.MM.yyyy HH:mm");
                    g.DrawString(subtitle, subFont, mutedBrush, 26, 48);
                }

                // Map area rectangle
                Rectangle mapRect = new Rectangle(30, 96, width - 60, height - 185);
                using (var cardBrush = new SolidBrush(Color.White))
                using (var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1.5f))
                {
                    g.FillRectangle(cardBrush, mapRect);
                    g.DrawRectangle(borderPen, mapRect);
                }

                Rectangle projRect = FitProjectionRect(mapRect, 24);

                // Build combined Czechia Kraje path for clipping & region outlines
                using (var krajePath = BuildPathFromRings(CzechGeoData.KrajePolygons, projRect))
                using (var orpPath = BuildPathFromRings(CzechGeoData.OrpPolygons, projRect))
                {
                    var validPts = new List<Tuple<PointF, double>>();
                    double sumAll = 0;
                    int countAll = 0;
                    double minVal = double.MaxValue;
                    double maxVal = double.MinValue;

                    foreach (var r in CzechData.Regions)
                    {
                        double? val;
                        valuesByRegion.TryGetValue(r.Code, out val);
                        if (val.HasValue)
                        {
                            PointF p = Project(r.Latitude, r.Longitude, projRect);
                            validPts.Add(Tuple.Create(p, val.Value));
                            sumAll += val.Value;
                            countAll++;
                            if (val.Value < minVal) minVal = val.Value;
                            if (val.Value > maxVal) maxVal = val.Value;
                        }
                    }

                    // Collect spatial grid points (real model grid) if available, otherwise fall back to region centroids
                    var spatialPts = new List<Tuple<PointF, double>>();
                    if (!string.IsNullOrEmpty(modelKey) && result.GridPoints != null && result.GridPoints.Count > 0)
                    {
                        foreach (var gp in result.GridPoints)
                        {
                            double? gVal = null;
                            if (modelKey == "prumer_modelu" || modelKey == "max_modelu" || modelKey == "rozptyl_modelu")
                            {
                                double gSum = 0;
                                double gMin = double.MaxValue;
                                double gMax = double.MinValue;
                                int gCnt = 0;
                                foreach (var m in result.Models)
                                {
                                    double? mv;
                                    if (gp.ModelValues.TryGetValue(m.Id, out mv) && mv.HasValue)
                                    {
                                        gSum += mv.Value;
                                        if (mv.Value < gMin) gMin = mv.Value;
                                        if (mv.Value > gMax) gMax = mv.Value;
                                        gCnt++;
                                    }
                                }
                                if (gCnt > 0)
                                {
                                    if (modelKey == "prumer_modelu") gVal = gSum / gCnt;
                                    else if (modelKey == "max_modelu") gVal = gMax;
                                    else gVal = gMax - gMin;
                                }
                            }
                            else
                            {
                                gp.ModelValues.TryGetValue(modelKey, out gVal);
                            }

                            if (gVal.HasValue)
                                spatialPts.Add(Tuple.Create(Project(gp.Latitude, gp.Longitude, projRect), gVal.Value));
                        }
                    }

                    bool hasDenseGrid = spatialPts.Count > 20;
                    var renderPts = hasDenseGrid ? spatialPts : validPts;

                    // 1. Clipped 1x1 px field via LockBits (fast direct memory write)
                    if (renderPts.Count > 0)
                    {
                        g.Flush();
                        RectangleF bounds = krajePath.GetBounds();
                        int startX = Math.Max(0, (int)Math.Floor(bounds.Left));
                        int endX = Math.Min(width - 1, (int)Math.Ceiling(bounds.Right));
                        int startY = Math.Max(0, (int)Math.Floor(bounds.Top));
                        int endY = Math.Min(height - 1, (int)Math.Ceiling(bounds.Bottom));

                        using (var maskBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                        using (var mg = Graphics.FromImage(maskBmp))
                        {
                            mg.Clear(Color.Black);
                            mg.FillPath(Brushes.White, krajePath);
                            mg.Flush();

                            Rectangle lockRect = new Rectangle(0, 0, width, height);
                            BitmapData bmpData = bmp.LockBits(lockRect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                            BitmapData maskData = maskBmp.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                            int totalBytes = Math.Abs(bmpData.Stride) * height;
                            byte[] pixels = new byte[totalBytes];
                            byte[] maskPixels = new byte[totalBytes];
                            Marshal.Copy(bmpData.Scan0, pixels, 0, totalBytes);
                            Marshal.Copy(maskData.Scan0, maskPixels, 0, totalBytes);

                            int stride = bmpData.Stride;
                            for (int y = startY; y <= endY; y++)
                            {
                                int rowOff = y * stride;
                                for (int x = startX; x <= endX; x++)
                                {
                                    int idx = rowOff + x * 4;
                                    if (maskPixels[idx] > 128)
                                    {
                                        double val = hasDenseGrid
                                            ? NearestGridValue(x + 0.5, y + 0.5, renderPts)
                                            : InterpolateIdw(x + 0.5, y + 0.5, renderPts);
                                        Color c = GetValueColor(val, result.Parameter);
                                        pixels[idx] = c.B;
                                        pixels[idx + 1] = c.G;
                                        pixels[idx + 2] = c.R;
                                        pixels[idx + 3] = 255;
                                    }
                                }
                            }

                            Marshal.Copy(pixels, 0, bmpData.Scan0, totalBytes);
                            maskBmp.UnlockBits(maskData);
                            bmp.UnlockBits(bmpData);
                        }
                    }
                    else
                    {
                        using (var emptyBrush = new SolidBrush(Color.FromArgb(241, 245, 249)))
                        {
                            g.FillPath(emptyBrush, krajePath);
                        }
                    }

                    // 2. Light-gray ORP (Obce s rozšířenou působností) underlay boundaries
                    using (var orpPen = new Pen(Color.FromArgb(135, 148, 163, 184), 0.85f))
                    {
                        orpPen.LineJoin = LineJoin.Round;
                        g.DrawPath(orpPen, orpPath);
                    }

                    // 3. Distinct Kraje & Czech Republic borders
                    using (var krajePen = new Pen(Color.FromArgb(30, 41, 59), 1.85f))
                    {
                        krajePen.LineJoin = LineJoin.Round;
                        g.DrawPath(krajePen, krajePath);
                    }

                    // 4. Regional capital cities & value pills
                    using (var valFont = new Font("Segoe UI", 9f, FontStyle.Bold))
                    using (var cityFont = new Font("Segoe UI", 8f, FontStyle.Regular))
                    using (var pillBg = new SolidBrush(Color.FromArgb(232, 255, 255, 255)))
                    using (var pillBorder = new Pen(Color.FromArgb(100, 116, 139), 1f))
                    using (var textBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                    using (var cityBrush = new SolidBrush(Color.FromArgb(220, 38, 38)))
                    using (var whitePen = new Pen(Color.White, 1.5f))
                    {
                        foreach (var r in CzechData.Regions)
                        {
                            PointF p = Project(r.Latitude, r.Longitude, projRect);
                            float offsetY = r.Code == "STC" ? 18f : -27f;

                            // Draw regional capital city marker & name below the dot
                            if (!string.IsNullOrEmpty(r.CapitalCity))
                            {
                                g.FillEllipse(cityBrush, p.X - 4f, p.Y - 4f, 8f, 8f);
                                g.DrawEllipse(whitePen, p.X - 4f, p.Y - 4f, 8f, 8f);
                                SizeF csz = g.MeasureString(r.CapitalCity, cityFont);
                                g.DrawString(r.CapitalCity, cityFont, textBrush, p.X - csz.Width / 2f, p.Y + 5f);
                            }

                            double? val;
                            valuesByRegion.TryGetValue(r.Code, out val);
                            string label = r.Code + ": " + (val.HasValue ? val.Value.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit : "N/A");
                            SizeF sz = g.MeasureString(label, valFont);

                            RectangleF pill = new RectangleF(p.X - sz.Width / 2f - 5f, p.Y + offsetY, sz.Width + 10f, sz.Height + 2f);
                            g.FillRectangle(pillBg, pill);
                            g.DrawRectangle(pillBorder, pill.X, pill.Y, pill.Width, pill.Height);
                            g.DrawString(label, valFont, textBrush, pill.X + 5f, pill.Y + 1f);
                        }
                    }

                    DrawLegendAndSummary(g, width, height, result.Parameter, paramInfo.LegendTitle, unit,
                        countAll > 0 ? (double?)(sumAll / countAll) : null,
                        countAll > 0 ? (double?)minVal : null,
                        countAll > 0 ? (double?)maxVal : null);
                }
            }

            return bmp;
        }

        private static GraphicsPath BuildPathFromRings(float[][] rings, Rectangle projRect)
        {
            var path = new GraphicsPath(FillMode.Winding);
            for (int r = 0; r < rings.Length; r++)
            {
                float[] coords = rings[r];
                int n = coords.Length / 2;
                if (n < 3) continue;
                PointF[] pts = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    pts[i] = Project(coords[i * 2 + 1], coords[i * 2], projRect);
                }
                path.AddPolygon(pts);
            }
            return path;
        }

        private static void DrawLegendAndSummary(Graphics g, int width, int height, WeatherParameter param, string legendTitle, string unit, double? avg, double? min, double? max)
        {
            int barY = height - 74;
            using (var labelFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var smallFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            using (var textBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
            {
                g.DrawString(legendTitle, labelFont, textBrush, 30, barY + 6);

                double[] stops = GetLegendStops(param);
                int x = 185;
                for (int i = 0; i < stops.Length; i++)
                {
                    Color c = GetValueColor(stops[i], param);
                    using (var b = new SolidBrush(c))
                    using (var p = new Pen(Color.FromArgb(148, 163, 184), 1f))
                    {
                        g.FillRectangle(b, x, barY + 5, 32, 18);
                        g.DrawRectangle(p, x, barY + 5, 32, 18);
                    }
                    string lbl = i == stops.Length - 1 ? stops[i].ToString(CultureInfo.InvariantCulture) + "+" : stops[i].ToString(CultureInfo.InvariantCulture);
                    g.DrawString(lbl, smallFont, textBrush, x + 2, barY + 26);
                    x += 36;
                }

                string stats = avg.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "Průměr ČR: {0:0.0} {3}   |   Min kraj: {1:0.0} {3}   |   Max kraj: {2:0.0} {3}", avg.Value, min.Value, max.Value, unit)
                    : "Data nejsou pro tento model k dispozici.";

                SizeF statsSize = g.MeasureString(stats, labelFont);
                g.DrawString(stats, labelFont, textBrush, width - 30 - statsSize.Width, barY + 12);
            }
        }

        private static double NearestGridValue(double x, double y, List<Tuple<PointF, double>> pts)
        {
            double bestDist = double.MaxValue;
            double bestVal = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double dx = x - pts[i].Item1.X;
                double dy = y - pts[i].Item1.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestDist)
                {
                    bestDist = d2;
                    bestVal = pts[i].Item2;
                }
            }
            return bestVal;
        }

        private static double InterpolateIdw(double x, double y, List<Tuple<PointF, double>> pts)
        {
            double num = 0;
            double den = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double dx = x - pts[i].Item1.X;
                double dy = y - pts[i].Item1.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < 1.0)
                    return pts[i].Item2;
                double w = 1.0 / (d2 * Math.Sqrt(d2));
                num += w * pts[i].Item2;
                den += w;
            }
            return den > 0 ? num / den : 0;
        }

        public static Color GetPrecipColor(double mm)
        {
            return GetValueColor(mm, WeatherParameter.TotalPrecipitation);
        }

        public static double[] GetLegendStops(WeatherParameter param)
        {
            switch (param)
            {
                case WeatherParameter.Snowfall:
                    return new double[] { 0, 0.1, 0.5, 1, 2, 5, 10, 15, 20, 35 };
                case WeatherParameter.Temperature2mMax:
                case WeatherParameter.Temperature2mMin:
                case WeatherParameter.Temperature850hPa:
                    return new double[] { -15, -10, -5, 0, 5, 10, 15, 20, 25, 30 };
                case WeatherParameter.CloudCover:
                    return new double[] { 0, 10, 25, 40, 55, 70, 85, 95 };
                case WeatherParameter.WindGusts10m:
                case WeatherParameter.WindSpeed10m:
                    return new double[] { 10, 20, 30, 40, 50, 65, 80, 100 };
                default:
                    return new double[] { 0, 0.1, 0.5, 1, 2, 5, 10, 15, 20, 30, 50 };
            }
        }

        public static Color GetValueColor(double val, WeatherParameter param)
        {
            switch (param)
            {
                case WeatherParameter.Snowfall:
                    if (val <= 0.05) return Color.FromArgb(241, 245, 249);
                    if (val < 0.5) return Color.FromArgb(224, 242, 254);
                    if (val < 1.0) return Color.FromArgb(186, 230, 253);
                    if (val < 2.0) return Color.FromArgb(125, 211, 252);
                    if (val < 5.0) return Color.FromArgb(147, 197, 253);
                    if (val < 10.0) return Color.FromArgb(165, 180, 252);
                    if (val < 15.0) return Color.FromArgb(192, 132, 252);
                    if (val < 20.0) return Color.FromArgb(217, 70, 239);
                    if (val < 35.0) return Color.FromArgb(232, 121, 249);
                    return Color.FromArgb(162, 28, 175);

                case WeatherParameter.Temperature2mMax:
                case WeatherParameter.Temperature2mMin:
                case WeatherParameter.Temperature850hPa:
                    if (val < -10.0) return Color.FromArgb(30, 58, 138);
                    if (val < -5.0) return Color.FromArgb(59, 130, 246);
                    if (val < 0.0) return Color.FromArgb(147, 197, 253);
                    if (val < 5.0) return Color.FromArgb(103, 232, 249);
                    if (val < 10.0) return Color.FromArgb(110, 231, 183);
                    if (val < 15.0) return Color.FromArgb(253, 224, 71);
                    if (val < 20.0) return Color.FromArgb(251, 191, 36);
                    if (val < 25.0) return Color.FromArgb(249, 115, 22);
                    if (val < 30.0) return Color.FromArgb(239, 68, 68);
                    return Color.FromArgb(185, 28, 28);

                case WeatherParameter.CloudCover:
                    if (val < 15.0) return Color.FromArgb(186, 230, 253);
                    if (val < 35.0) return Color.FromArgb(226, 232, 240);
                    if (val < 55.0) return Color.FromArgb(203, 213, 225);
                    if (val < 75.0) return Color.FromArgb(148, 163, 184);
                    if (val < 90.0) return Color.FromArgb(100, 116, 139);
                    return Color.FromArgb(71, 85, 105);

                case WeatherParameter.WindGusts10m:
                case WeatherParameter.WindSpeed10m:
                    if (val < 20.0) return Color.FromArgb(220, 252, 231);
                    if (val < 35.0) return Color.FromArgb(134, 239, 172);
                    if (val < 50.0) return Color.FromArgb(253, 224, 71);
                    if (val < 65.0) return Color.FromArgb(251, 146, 60);
                    if (val < 80.0) return Color.FromArgb(239, 68, 68);
                    if (val < 100.0) return Color.FromArgb(192, 38, 211);
                    return Color.FromArgb(126, 34, 206);

                default:
                    if (val <= 0.05) return Color.FromArgb(241, 245, 249);
                    if (val < 0.5) return Color.FromArgb(226, 240, 254);
                    if (val < 1.0) return Color.FromArgb(191, 219, 254);
                    if (val < 2.0) return Color.FromArgb(147, 197, 253);
                    if (val < 5.0) return Color.FromArgb(96, 165, 250);
                    if (val < 10.0) return Color.FromArgb(56, 189, 248);
                    if (val < 15.0) return Color.FromArgb(45, 212, 191);
                    if (val < 20.0) return Color.FromArgb(52, 211, 153);
                    if (val < 30.0) return Color.FromArgb(250, 204, 21);
                    if (val < 50.0) return Color.FromArgb(249, 115, 22);
                    return Color.FromArgb(225, 29, 72);
            }
        }

        private static Rectangle FitProjectionRect(Rectangle outer, int pad)
        {
            int w = outer.Width - pad * 2;
            int h = outer.Height - pad * 2;
            double lonSpan = MaxLon - MinLon;
            double latSpan = MaxLat - MinLat;
            double cosLat = Math.Cos(49.8 * Math.PI / 180.0);
            double desiredAspect = (lonSpan * cosLat) / latSpan;
            double actualAspect = (double)w / h;

            if (actualAspect > desiredAspect)
            {
                int newW = (int)(h * desiredAspect);
                return new Rectangle(outer.Left + pad + (w - newW) / 2, outer.Top + pad, newW, h);
            }
            else
            {
                int newH = (int)(w / desiredAspect);
                return new Rectangle(outer.Left + pad, outer.Top + pad + (h - newH) / 2, w, newH);
            }
        }

        private static PointF Project(double lat, double lon, Rectangle rect)
        {
            float x = (float)(rect.Left + ((lon - MinLon) / (MaxLon - MinLon)) * rect.Width);
            float y = (float)(rect.Bottom - ((lat - MinLat) / (MaxLat - MinLat)) * rect.Height);
            return new PointF(x, y);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
