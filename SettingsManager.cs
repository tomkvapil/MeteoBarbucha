using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MeteoSrazky
{
    public class AppSettings
    {
        public string OutputDir { get; set; }
        public List<string> SelectedModelIds { get; set; }
        public int AggModeIndex { get; set; }
        public int ForecastDaysIndex { get; set; }
        public int ParamIndex { get; set; }
        public int AutoHoursIndex { get; set; }
        public string MeteologixApiKey { get; set; }
        public string UpdateManifestUrl { get; set; }
        public double UtciWarningThreshold { get; set; }

        public AppSettings()
        {
            SelectedModelIds = new List<string>();
            ForecastDaysIndex = 2; // 3 dny
            AutoHoursIndex = 1; // 3 h
            UtciWarningThreshold = 32.0;
            UpdateManifestUrl = UpdateService.DefaultManifestUrl;
        }
    }

    public static class SettingsManager
    {
        private static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MeteoBarbucha", "settings.json");
        }

        public static AppSettings Load()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path))
                    return null;

                string json = File.ReadAllText(path, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                return ser.Deserialize<AppSettings>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                string path = GetSettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(settings);
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch
            {
                // Ignore non-critical settings persistence errors
            }
        }
    }
}
