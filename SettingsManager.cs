using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MeteoSrazky
{
    public class AppSettings
    {
        public string OutputDir { get; set; }
        public int AggModeIndex { get; set; }
        public int ForecastDaysIndex { get; set; }
        public int ParamIndex { get; set; }
        public int AutoHoursIndex { get; set; }
        public bool AutoRunEnabled { get; set; }
        public int DataSourceIndex { get; set; }
        public string UpdateManifestUrl { get; set; }
        public double UtciWarningThreshold { get; set; }

        // Encrypted DPAPI fields stored on disk
        public string MeteologixApiKeyProtected { get; set; }
        public string OpenMeteoKeyOrUrlProtected { get; set; }

        // Legacy plaintext fields kept only for automatic one-way migration on Load()
        public string MeteologixApiKey { get; set; }
        public string OpenMeteoCustomKeyOrUrl { get; set; }

        public AppSettings()
        {
            ForecastDaysIndex = 2; // 3 dny
            AutoHoursIndex = 1;    // 3 h
            UtciWarningThreshold = 30.0;
            UpdateManifestUrl = UpdateService.DefaultManifestUrl;
        }
    }

    public static class SettingsManager
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MeteoBarbuchaCZ_DPAPI_v1");

        private static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MeteoBarbucha", "settings.json");
        }

        private static string ProtectSecret(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "";
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(plainText);
                byte[] enc = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch
            {
                return "";
            }
        }

        private static string UnprotectSecret(string base64Protected)
        {
            if (string.IsNullOrEmpty(base64Protected))
                return "";
            try
            {
                byte[] enc = Convert.FromBase64String(base64Protected);
                byte[] raw = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch
            {
                return "";
            }
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
                var s = ser.Deserialize<AppSettings>(json);
                if (s == null) return null;

                bool migratedPlaintext = false;

                if (!string.IsNullOrEmpty(s.MeteologixApiKeyProtected))
                {
                    s.MeteologixApiKey = UnprotectSecret(s.MeteologixApiKeyProtected);
                }
                else if (!string.IsNullOrEmpty(s.MeteologixApiKey))
                {
                    migratedPlaintext = true;
                }

                if (!string.IsNullOrEmpty(s.OpenMeteoKeyOrUrlProtected))
                {
                    s.OpenMeteoCustomKeyOrUrl = UnprotectSecret(s.OpenMeteoKeyOrUrlProtected);
                }
                else if (!string.IsNullOrEmpty(s.OpenMeteoCustomKeyOrUrl))
                {
                    migratedPlaintext = true;
                }

                if (migratedPlaintext)
                {
                    Save(s);
                }

                return s;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null) return;
            try
            {
                string mlxPlain = settings.MeteologixApiKey;
                string omPlain = settings.OpenMeteoCustomKeyOrUrl;

                var toSave = new AppSettings
                {
                    OutputDir = settings.OutputDir,
                    AggModeIndex = settings.AggModeIndex,
                    ForecastDaysIndex = settings.ForecastDaysIndex,
                    ParamIndex = settings.ParamIndex,
                    AutoHoursIndex = settings.AutoHoursIndex,
                    AutoRunEnabled = settings.AutoRunEnabled,
                    DataSourceIndex = settings.DataSourceIndex,
                    UpdateManifestUrl = settings.UpdateManifestUrl,
                    UtciWarningThreshold = settings.UtciWarningThreshold,
                    MeteologixApiKeyProtected = ProtectSecret(mlxPlain),
                    OpenMeteoKeyOrUrlProtected = ProtectSecret(omPlain),
                    MeteologixApiKey = null,
                    OpenMeteoCustomKeyOrUrl = null
                };

                string path = GetSettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(toSave);
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Nepodařilo se uložit settings.json: " + ex.Message);
            }
        }
    }
}
