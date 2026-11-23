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

        private static readonly object _cacheLock = new object();
        private static int _cachedGridCount = -1;
        private static byte[] _cachedMask;
        private static int[] _cachedIdx;     // 4 indices per pixel
        private static float[] _cachedWeights; // 4 weights per pixel
        private static int _cacheStartX, _cacheEndX, _cacheStartY, _cacheEndY;

        private static void EnsureGridCache(List<GeoGridPoint> gridPoints, Rectangle projRect, GraphicsPath krajePath)
        {
            lock (_cacheLock)
            {
                if (_cachedGridCount == gridPoints.Count && _cachedMask != null)
                    return;

                int w = MapWidth;
                int h = MapHeight;
                int nPix = w * h;
                byte[] mask = new byte[nPix];
                int[] indices = new int[nPix * 4];
                float[] weights = new float[nPix * 4];

                RectangleF bounds = krajePath.GetBounds();
                _cacheStartX = Math.Max(0, (int)Math.Floor(bounds.Left));
                _cacheEndX = Math.Min(w - 1, (int)Math.Ceiling(bounds.Right));
                _cacheStartY = Math.Max(0, (int)Math.Floor(bounds.Top));
                _cacheEndY = Math.Min(h - 1, (int)Math.Ceiling(bounds.Bottom));

                PointF[] gridScreen = new PointF[gridPoints.Count];
                for (int i = 0; i < gridPoints.Count; i++)
                    gridScreen[i] = Project(gridPoints[i].Latitude, gridPoints[i].Longitude, projRect);

                using (var maskBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (var mg = Graphics.FromImage(maskBmp))
                {
                    mg.Clear(Color.Black);
                    mg.FillPath(Brushes.White, krajePath);
                    mg.Flush();

                    BitmapData maskData = maskBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    byte[] rawMask = new byte[Math.Abs(maskData.Stride) * h];
                    Marshal.Copy(maskData.Scan0, rawMask, 0, rawMask.Length);
                    int stride = maskData.Stride;

                    for (int y = _cacheStartY; y <= _cacheEndY; y++)
                    {
                        int rowOff = y * stride;
                        int pixRow = y * w;
                        for (int x = _cacheStartX; x <= _cacheEndX; x++)
                        {
                            if (rawMask[rowOff + x * 4] <= 128) continue;
                            int pIdx = pixRow + x;
                            mask[pIdx] = 1;

                            // Find 4 nearest grid points for smooth local IDW interpolation
                            double d0 = double.MaxValue, d1 = double.MaxValue, d2 = double.MaxValue, d3 = double.MaxValue;
                            int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
                            double px = x + 0.5, py = y + 0.5;

                            for (int g = 0; g < gridScreen.Length; g++)
                            {
                                double dx = px - gridScreen[g].X;
                                double dy = py - gridScreen[g].Y;
                                double dist2 = dx * dx + dy * dy;
                                if (dist2 < d0)
                                {
                                    d3 = d2; i3 = i2;
                                    d2 = d1; i2 = i1;
                                    d1 = d0; i1 = i0;
                                    d0 = dist2; i0 = g;
                                }
                                else if (dist2 < d1)
                                {
                                    d3 = d2; i3 = i2;
                                    d2 = d1; i2 = i1;
                                    d1 = dist2; i1 = g;
                                }
                                else if (dist2 < d2)
                                {
                                    d3 = d2; i3 = i2;
                                    d2 = dist2; i2 = g;
                                }
                                else if (dist2 < d3)
                                {
                                    d3 = dist2; i3 = g;
                                }
                            }

                            int base4 = pIdx * 4;
                            indices[base4] = i0;
                            indices[base4 + 1] = i1;
                            indices[base4 + 2] = i2;
                            indices[base4 + 3] = i3;

                            if (d0 < 0.5)
                            {
                                weights[base4] = 1f;
                            }
                            else
                            {
                                double w0 = 1.0 / d0;
                                double w1 = 1.0 / d1;
                                double w2 = 1.0 / d2;
                                double w3 = 1.0 / d3;
                                double wSum = w0 + w1 + w2 + w3;
                                weights[base4] = (float)(w0 / wSum);
                                weights[base4 + 1] = (float)(w1 / wSum);
                                weights[base4 + 2] = (float)(w2 / wSum);
                                weights[base4 + 3] = (float)(w3 / wSum);
                            }
                        }
                    }

                    maskBmp.UnlockBits(maskData);
                }

                _cachedMask = mask;
                _cachedIdx = indices;
                _cachedWeights = weights;
                _cachedGridCount = gridPoints.Count;
            }
        }

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
                    string dateSpan;
                    if (result.SelectedTimeStepIndex >= 0 && result.TimeSteps != null && result.SelectedTimeStepIndex < result.TimeSteps.Count)
                    {
                        dateSpan = "Krok: " + result.TimeSteps[result.SelectedTimeStepIndex].Replace("T", " ");
                    }
                    else
                    {
                        dateSpan = !string.IsNullOrEmpty(result.PeriodDescription)
                            ? result.PeriodDescription
                            : (result.Dates.Count > 0
                                ? result.Dates[0] + " až " + result.Dates[result.Dates.Count - 1] + " (" + result.ForecastDays + " dny)"
                                : result.ForecastDays + " dní");
                    }
                    string runInfo = "";
                    if (!string.IsNullOrEmpty(modelKey) && result.ModelRunTimes != null)
                    {
                        string rt;
                        if (result.ModelRunTimes.TryGetValue(modelKey, out rt) && !string.IsNullOrEmpty(rt))
                            runInfo = "   |   Běh modelu: " + rt;
                        else if (modelKey == "prumer_modelu" || modelKey == "max_modelu" || modelKey == "rozptyl_modelu")
                        {
                            foreach (var m in result.Models)
                            {
                                if (result.ModelRunTimes.TryGetValue(m.Id, out rt) && !string.IsNullOrEmpty(rt))
                                {
                                    runInfo = "   |   Běh modelů: " + rt;
                                    break;
                                }
                            }
                        }
                    }

                    string subtitle = "Období: " + dateSpan + runInfo + "   |   Vygenerováno: " + result.FetchedAt.ToString("dd.MM.yyyy HH:mm");
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

                    // Collect spatial grid values (ordered 1:1 with result.GridPoints for fast cache lookup)
                    bool canUseGridCache = !string.IsNullOrEmpty(modelKey) && result.GridPoints != null && result.GridPoints.Count > 20;
                    double?[] gridVals = canUseGridCache ? new double?[result.GridPoints.Count] : null;
                    int validGridCount = 0;

                    if (canUseGridCache)
                    {
                        for (int gi = 0; gi < result.GridPoints.Count; gi++)
                        {
                            var gp = result.GridPoints[gi];
                            double? gVal = null;
                            if (modelKey == "prumer_modelu" || modelKey == "max_modelu" || modelKey == "rozptyl_modelu")
                            {
                                double gSum = 0, gMin = double.MaxValue, gMax = double.MinValue;
                                int gCnt = 0;
                                foreach (var m in result.Models)
                                {
                                    double? mv = WeatherService.GetGridPointValue(gp, m.Id, result.SelectedTimeStepIndex);
                                    if (mv.HasValue)
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
                                gVal = WeatherService.GetGridPointValue(gp, modelKey, result.SelectedTimeStepIndex);
                            }

                            gridVals[gi] = gVal;
                            if (gVal.HasValue) validGridCount++;
                        }
                    }

                    // 1. Clipped 1x1 px field via Precomputed 4-Neighbor IDW Cache + LockBits
                    if (canUseGridCache && validGridCount > 0)
                    {
                        g.Flush();
                        EnsureGridCache(result.GridPoints, projRect, krajePath);
                        bool categorical = result.Parameter == WeatherParameter.SignificantWeather;

                        BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                        int totalBytes = Math.Abs(bmpData.Stride) * height;
                        byte[] pixels = new byte[totalBytes];
                        Marshal.Copy(bmpData.Scan0, pixels, 0, totalBytes);
                        int stride = bmpData.Stride;

                        for (int y = _cacheStartY; y <= _cacheEndY; y++)
                        {
                            int rowOff = y * stride;
                            int pixRow = y * width;
                            for (int x = _cacheStartX; x <= _cacheEndX; x++)
                            {
                                int pIdx = pixRow + x;
                                if (_cachedMask[pIdx] == 0) continue;

                                int base4 = pIdx * 4;
                                Color c;
                                double warnVal;

                                if (categorical)
                                {
                                    double rSum = 0, gSum = 0, bSum = 0, wSum = 0;
                                    double? v0 = gridVals[_cachedIdx[base4]];
                                    warnVal = v0 ?? 0.0;
                                    for (int k = 0; k < 4; k++)
                                    {
                                        double? vk = gridVals[_cachedIdx[base4 + k]];
                                        if (vk.HasValue)
                                        {
                                            float wk = _cachedWeights[base4 + k];
                                            Color ck = GetValueColor(vk.Value, result.Parameter);
                                            rSum += ck.R * wk;
                                            gSum += ck.G * wk;
                                            bSum += ck.B * wk;
                                            wSum += wk;
                                        }
                                    }
                                    c = wSum > 0
                                        ? Color.FromArgb((int)(rSum / wSum), (int)(gSum / wSum), (int)(bSum / wSum))
                                        : Color.FromArgb(241, 245, 249);
                                }
                                else
                                {
                                    double sum = 0;
                                    double wSum = 0;
                                    for (int k = 0; k < 4; k++)
                                    {
                                        double? vk = gridVals[_cachedIdx[base4 + k]];
                                        if (vk.HasValue)
                                        {
                                            float wk = _cachedWeights[base4 + k];
                                            sum += vk.Value * wk;
                                            wSum += wk;
                                        }
                                    }
                                    warnVal = wSum > 0 ? (sum / wSum) : 0.0;
                                    c = GetValueColor(warnVal, result.Parameter);
                                }

                                if (result.WarningThreshold.HasValue && warnVal >= result.WarningThreshold.Value && ((x + y) % 7 < 2))
                                    c = Color.FromArgb(220, 38, 38);

                                int idx = rowOff + x * 4;
                                pixels[idx] = c.B;
                                pixels[idx + 1] = c.G;
                                pixels[idx + 2] = c.R;
                                pixels[idx + 3] = 255;
                            }
                        }

                        Marshal.Copy(pixels, 0, bmpData.Scan0, totalBytes);
                        bmp.UnlockBits(bmpData);
                    }
                    else if (validPts.Count > 0)
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
                                        double val = InterpolateIdw(x + 0.5, y + 0.5, validPts);
                                        Color c = GetValueColor(val, result.Parameter);
                                        if (result.WarningThreshold.HasValue && val >= result.WarningThreshold.Value && ((x + y) % 7 < 2))
                                            c = Color.FromArgb(220, 38, 38);

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
                            bool isWarn = result.WarningThreshold.HasValue &&
                                          val.HasValue &&
                                          val.Value >= result.WarningThreshold.Value;

                            string valStr = !val.HasValue
                                ? "N/A"
                                : (result.Parameter == WeatherParameter.SignificantWeather
                                    ? CzechData.GetWmoShortLabel(val.Value)
                                    : val.Value.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit);
                            string label = (isWarn ? "⚠ " : "") + r.Code + ": " + valStr;
                            SizeF sz = g.MeasureString(label, valFont);

                            RectangleF pill = new RectangleF(p.X - sz.Width / 2f - 5f, p.Y + offsetY, sz.Width + 10f, sz.Height + 2f);
                            if (isWarn)
                            {
                                using (var warnBg = new SolidBrush(Color.FromArgb(245, 254, 226, 226)))
                                using (var warnPen = new Pen(Color.FromArgb(220, 38, 38), 1.8f))
                                {
                                    g.FillRectangle(warnBg, pill);
                                    g.DrawRectangle(warnPen, pill.X, pill.Y, pill.Width, pill.Height);
                                }
                            }
                            else
                            {
                                g.FillRectangle(pillBg, pill);
                                g.DrawRectangle(pillBorder, pill.X, pill.Y, pill.Width, pill.Height);
                            }
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

                if (param == WeatherParameter.SignificantWeather)
                {
                    var sigStops = new[] {
                        Tuple.Create(0.0, "Jasno"),
                        Tuple.Create(3.0, "Oblačno"),
                        Tuple.Create(45.0, "Mlha"),
                        Tuple.Create(53.0, "Mrhol."),
                        Tuple.Create(63.0, "Déšť"),
                        Tuple.Create(73.0, "Sníh"),
                        Tuple.Create(81.0, "Přeh."),
                        Tuple.Create(95.0, "Bouřka")
                    };
                    int sx = 120;
                    foreach (var st in sigStops)
                    {
                        Color c = GetValueColor(st.Item1, param);
                        using (var b = new SolidBrush(c))
                        using (var p = new Pen(Color.FromArgb(148, 163, 184), 1f))
                        {
                            g.FillRectangle(b, sx, barY + 5, 42, 18);
                            g.DrawRectangle(p, sx, barY + 5, 42, 18);
                        }
                        g.DrawString(st.Item2, smallFont, textBrush, sx + 1, barY + 26);
                        sx += 48;
                    }
                    string sigStats = max.HasValue
                        ? "Nejvýraznější jev v ČR: " + CzechData.GetWmoShortLabel(max.Value) + " (WMO " + ((int)Math.Round(max.Value)) + ")"
                        : "Data nejsou pro tento model k dispozici.";
                    SizeF sigSize = g.MeasureString(sigStats, labelFont);
                    g.DrawString(sigStats, labelFont, textBrush, width - 30 - sigSize.Width, barY + 12);
                    return;
                }

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
                case WeatherParameter.UtciMax:
                    return new double[] { -13, 0, 9, 20, 26, 32, 38, 46 };
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

                case WeatherParameter.UtciMax:
                    if (val < -13.0) return Color.FromArgb(30, 58, 138);   // Strong cold stress
                    if (val < 0.0) return Color.FromArgb(59, 130, 246);    // Moderate cold stress
                    if (val < 9.0) return Color.FromArgb(147, 197, 253);   // Slight cold stress
                    if (val < 26.0) return Color.FromArgb(134, 239, 172);  // No thermal stress
                    if (val < 32.0) return Color.FromArgb(253, 224, 71);   // Moderate heat stress
                    if (val < 38.0) return Color.FromArgb(249, 115, 22);   // Strong heat stress
                    if (val < 46.0) return Color.FromArgb(220, 38, 38);    // Very strong heat stress
                    return Color.FromArgb(153, 27, 27);                    // Extreme heat stress

                case WeatherParameter.SignificantWeather:
                    int wmo = (int)Math.Round(val);
                    if (wmo <= 1) return Color.FromArgb(254, 249, 195);    // Clear / mostly clear
                    if (wmo <= 3) return Color.FromArgb(203, 213, 225);    // Cloudy / overcast
                    if (wmo <= 48) return Color.FromArgb(253, 230, 138);   // Fog
                    if (wmo <= 57) return Color.FromArgb(167, 243, 208);   // Drizzle
                    if (wmo <= 67) return Color.FromArgb(56, 189, 248);    // Rain / freezing rain
                    if (wmo <= 77) return Color.FromArgb(192, 132, 252);   // Snow
                    if (wmo <= 86) return Color.FromArgb(168, 85, 247);    // Showers
                    return Color.FromArgb(239, 68, 68);                    // Thunderstorm

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
