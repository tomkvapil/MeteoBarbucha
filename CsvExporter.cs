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

            sb.Append("Kód;Kraj");
            foreach (var model in result.Models)
            {
                string note = (result.ModelNotes != null && result.ModelNotes.ContainsKey(model.Id)) ? " [odvozeno]" : "";
                sb.Append(";").Append(EscapeCsv(model.DisplayName + note + " (" + unit + ")"));
            }
            sb.AppendLine(";Průměr (" + unit + ");Medián P50 (" + unit + ");P10 (" + unit + ");P90 (" + unit + ");Max (" + unit + ");Shoda nad prahem (%)");

            var modelTotals = new Dictionary<string, double>();
            var modelCounts = new Dictionary<string, int>();
            foreach (var m in result.Models)
            {
                modelTotals[m.Id] = 0;
                modelCounts[m.Id] = 0;
            }

            var rowVals = new List<double>();
            foreach (var region in CzechData.Regions)
            {
                sb.Append(EscapeCsv(region.Code)).Append(";").Append(EscapeCsv(region.Name));
                rowVals.Clear();

                var regionMap = regionalTable[region.Code];
                foreach (var model in result.Models)
                {
                    double? val;
                    regionMap.TryGetValue(model.Id, out val);
                    sb.Append(";");
                    if (val.HasValue)
                    {
                        sb.Append(val.Value.ToString("0.00", czCulture));
                        rowVals.Add(val.Value);
                        modelTotals[model.Id] += val.Value;
                        modelCounts[model.Id]++;
                    }
                }

                int hits, tot;
                double? mean = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyMean, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot);
                double? med = EnsembleStats.ComputePercentile(rowVals, 50.0);
                double? p10 = EnsembleStats.ComputePercentile(rowVals, 10.0);
                double? p90 = EnsembleStats.ComputePercentile(rowVals, 90.0);
                double? mx = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyMax, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot);
                double? ag = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyAgreement, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot);

                sb.Append(";").Append(mean.HasValue ? mean.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(med.HasValue ? med.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(p10.HasValue ? p10.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(p90.HasValue ? p90.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(mx.HasValue ? mx.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(ag.HasValue ? ag.Value.ToString("0.0", czCulture) : "");
                sb.AppendLine();
            }

            sb.Append("CR;Průměr ČR");
            var crAvgs = new List<double>();
            foreach (var model in result.Models)
            {
                sb.Append(";");
                if (modelCounts[model.Id] > 0)
                {
                    double avg = modelTotals[model.Id] / modelCounts[model.Id];
                    sb.Append(avg.ToString("0.00", czCulture));
                    crAvgs.Add(avg);
                }
            }

            int ch, ct2;
            double? crMean = EnsembleStats.ComputeSummaryFromValues(crAvgs, EnsembleStats.KeyMean, result.EffectiveAgreementThreshold, result.Parameter, out ch, out ct2);
            double? crMed = EnsembleStats.ComputePercentile(crAvgs, 50.0);
            sb.Append(";").Append(crMean.HasValue ? crMean.Value.ToString("0.00", czCulture) : "");
            sb.Append(";").Append(crMed.HasValue ? crMed.Value.ToString("0.00", czCulture) : "");
            sb.AppendLine(";;;;");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
            return filePath;
        }

        public static string ExportOrpToCsv(string filePath, ForecastResult result, RegionAggregationMode mode)
        {
            var sb = new StringBuilder();
            var czCulture = CultureInfo.GetCultureInfo("cs-CZ");
            string unit = string.IsNullOrEmpty(result.Unit) ? "mm" : result.Unit;

            var orps = OrpCatalog.GetAllOrps();
            var orpTable = OrpCatalog.ComputeOrpTable(result, mode);

            sb.Append("Kraj;ORP");
            foreach (var model in result.Models)
            {
                sb.Append(";").Append(EscapeCsv(model.DisplayName + " (" + unit + ")"));
            }
            sb.AppendLine(";Medián P50 (" + unit + ");P10 (" + unit + ");P90 (" + unit + ");Max (" + unit + ");Shoda nad prahem (%)");

            var rowVals = new List<double>();
            foreach (var orp in orps)
            {
                sb.Append(EscapeCsv(orp.RegionCode)).Append(";").Append(EscapeCsv(orp.Name));
                rowVals.Clear();

                var rowMap = orpTable[orp.Name];
                foreach (var model in result.Models)
                {
                    double? val;
                    rowMap.TryGetValue(model.Id, out val);
                    sb.Append(";");
                    if (val.HasValue)
                    {
                        sb.Append(val.Value.ToString("0.00", czCulture));
                        rowVals.Add(val.Value);
                    }
                }

                int hits, tot;
                double? med = EnsembleStats.ComputePercentile(rowVals, 50.0);
                double? p10 = EnsembleStats.ComputePercentile(rowVals, 10.0);
                double? p90 = EnsembleStats.ComputePercentile(rowVals, 90.0);
                double? mx = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyMax, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot);
                double? ag = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyAgreement, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot);

                sb.Append(";").Append(med.HasValue ? med.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(p10.HasValue ? p10.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(p90.HasValue ? p90.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(mx.HasValue ? mx.Value.ToString("0.00", czCulture) : "");
                sb.Append(";").Append(ag.HasValue ? ag.Value.ToString("0.0", czCulture) : "");
                sb.AppendLine();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
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
