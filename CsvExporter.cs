using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MeteoSrazky
{
    public static class CsvExporter
    {
        public static string ExportToCsv(string filePath, ForecastResult result)
        {
            return ExportToCsv(filePath, result, RegionAggregationMode.PointValue);
        }

        public static string ExportToCsv(string filePath, ForecastResult result, RegionAggregationMode mode)
        {
            var sb = new StringBuilder();
            var czCulture = CultureInfo.GetCultureInfo("cs-CZ");

            string unit = string.IsNullOrEmpty(result.Unit) ? "mm" : result.Unit;
            var regionalTable = WeatherService.ComputeRegionalTable(result, mode);

            // Header row
            sb.Append("Kód;Kraj");
            foreach (var model in result.Models)
            {
                sb.Append(";").Append(EscapeCsv(model.DisplayName + " (" + unit + ")"));
            }
            sb.AppendLine(";Průměr modelů (" + unit + ");Min (" + unit + ");Max (" + unit + ")");

            // Data rows per region
            var modelTotals = new Dictionary<string, double>();
            var modelCounts = new Dictionary<string, int>();
            foreach (var m in result.Models)
            {
                modelTotals[m.Id] = 0;
                modelCounts[m.Id] = 0;
            }

            foreach (var region in CzechData.Regions)
            {
                sb.Append(EscapeCsv(region.Code)).Append(";").Append(EscapeCsv(region.Name));

                double rowSum = 0;
                int rowCount = 0;
                double rowMin = double.MaxValue;
                double rowMax = double.MinValue;

                var regionMap = regionalTable[region.Code];
                foreach (var model in result.Models)
                {
                    double? val;
                    regionMap.TryGetValue(model.Id, out val);
                    sb.Append(";");
                    if (val.HasValue)
                    {
                        sb.Append(val.Value.ToString("0.00", czCulture));
                        rowSum += val.Value;
                        rowCount++;
                        if (val.Value < rowMin) rowMin = val.Value;
                        if (val.Value > rowMax) rowMax = val.Value;

                        modelTotals[model.Id] += val.Value;
                        modelCounts[model.Id]++;
                    }
                }

                sb.Append(";");
                if (rowCount > 0) sb.Append((rowSum / rowCount).ToString("0.00", czCulture));
                sb.Append(";");
                if (rowCount > 0) sb.Append(rowMin.ToString("0.00", czCulture));
                sb.Append(";");
                if (rowCount > 0) sb.Append(rowMax.ToString("0.00", czCulture));
                sb.AppendLine();
            }

            // Summary row: ČR Průměr
            sb.Append("CR;Průměr ČR");
            double grandSum = 0;
            int grandCount = 0;
            foreach (var model in result.Models)
            {
                sb.Append(";");
                if (modelCounts[model.Id] > 0)
                {
                    double avg = modelTotals[model.Id] / modelCounts[model.Id];
                    sb.Append(avg.ToString("0.00", czCulture));
                    grandSum += avg;
                    grandCount++;
                }
            }
            sb.Append(";");
            if (grandCount > 0) sb.Append((grandSum / grandCount).ToString("0.00", czCulture));
            sb.AppendLine(";;");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            // UTF-8 with BOM for seamless Czech Excel compatibility
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
            return filePath;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.Contains(";") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
