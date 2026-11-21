using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Notes { get; set; }
        public bool IsNewer { get; set; }
    }

    public static class UpdateService
    {
        public const string CurrentVersion = "1.0.0";

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("MeteoBarbuchaUpdater/1.0");
            return c;
        }

        public static async Task<UpdateInfo> CheckForUpdateAsync(string manifestUrl)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
                throw new ArgumentException("Není nastavena URL adresa souboru version.json.");

            string json = await _http.GetStringAsync(manifestUrl.Trim());
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
                throw new Exception("Neplatný formát JSON manifestu.");

            string remoteVer = null;
            string downloadUrl = null;
            string notes = "";

            // Support both simple version.json AND GitHub Releases API (/releases/latest)
            if (root.ContainsKey("tag_name"))
            {
                remoteVer = Convert.ToString(root["tag_name"]).TrimStart('v', 'V');
                notes = root.ContainsKey("body") ? Convert.ToString(root["body"]) : "";
                if (root.ContainsKey("assets"))
                {
                    var assets = root["assets"] as object[];
                    if (assets != null)
                    {
                        foreach (Dictionary<string, object> asset in assets)
                        {
                            string name = asset.ContainsKey("name") ? Convert.ToString(asset["name"]) : "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && asset.ContainsKey("browser_download_url"))
                            {
                                downloadUrl = Convert.ToString(asset["browser_download_url"]);
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                remoteVer = root.ContainsKey("version") ? Convert.ToString(root["version"]).TrimStart('v', 'V') : "";
                downloadUrl = root.ContainsKey("downloadUrl") ? Convert.ToString(root["downloadUrl"]) : "";
                notes = root.ContainsKey("notes") ? Convert.ToString(root["notes"]) : "";
            }

            if (string.IsNullOrWhiteSpace(remoteVer) || string.IsNullOrWhiteSpace(downloadUrl))
                throw new Exception("Manifest neobsahuje platnou verzi nebo odkaz ke stažení (.exe).");

            Version curV, remV;
            bool isNewer = Version.TryParse(CurrentVersion, out curV) && Version.TryParse(remoteVer, out remV)
                ? remV > curV
                : !string.Equals(CurrentVersion, remoteVer, StringComparison.OrdinalIgnoreCase);

            return new UpdateInfo
            {
                Version = remoteVer,
                DownloadUrl = downloadUrl,
                Notes = notes,
                IsNewer = isNewer
            };
        }

        public static async Task DownloadAndApplyUpdateAsync(string downloadUrl)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            string newExePath = exePath + ".new";

            byte[] bytes = await _http.GetByteArrayAsync(downloadUrl);
            if (bytes == null || bytes.Length < 50000)
                throw new Exception("Stažený soubor aktualizace je neplatný nebo poškozený.");

            File.WriteAllBytes(newExePath, bytes);

            string batPath = Path.Combine(Path.GetTempPath(), "meteobarbucha_update_" + Guid.NewGuid().ToString("N") + ".bat");
            string batContent = string.Format(
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "move /Y \"{0}\" \"{1}\"\r\n" +
                "start \"\" \"{1}\"\r\n" +
                "del \"%~f0\"\r\n",
                newExePath,
                exePath
            );
            File.WriteAllText(batPath, batContent, Encoding.Default);

            var psi = new ProcessStartInfo
            {
                FileName = batPath,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
            Application.Exit();
        }
    }
}
