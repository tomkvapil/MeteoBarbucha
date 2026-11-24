using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public static class AppLogger
    {
        private static readonly object _lock = new object();
        private static readonly List<string> _recentErrors = new List<string>();
        private const int MaxLogBytes = 1024 * 1024; // 1 MB

        public static string GetLogDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MeteoBarbucha", "logs");
        }

        public static string GetLogFilePath()
        {
            return Path.Combine(GetLogDirectory(), "meteobarbucha.log");
        }

        public static void Info(string message)
        {
            WriteEntry("INFO", message, false);
        }

        public static void Warn(string message)
        {
            WriteEntry("WARN", message, true);
        }

        public static void Error(string message)
        {
            WriteEntry("ERROR", message, true);
        }

        private static void WriteEntry(string level, string message, bool trackInDiag)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}",
                DateTime.Now, level, message ?? "");

            lock (_lock)
            {
                if (trackInDiag)
                {
                    _recentErrors.Add(line);
                    while (_recentErrors.Count > 25)
                        _recentErrors.RemoveAt(0);
                }

                try
                {
                    string dir = GetLogDirectory();
                    Directory.CreateDirectory(dir);
                    string path = GetLogFilePath();

                    if (File.Exists(path))
                    {
                        var fi = new FileInfo(path);
                        if (fi.Length > MaxLogBytes)
                        {
                            string oldPath = path + ".old";
                            if (File.Exists(oldPath))
                                File.Delete(oldPath);
                            File.Move(path, oldPath);
                        }
                    }

                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Ignore file I/O errors in logger
                }
            }
        }

        public static string BuildDiagnosticReport(ForecastResult lastResult, DataSourcePreference activeSource)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== DIAGNOSTIKA APLIKACE METEOBARBUCHA ČR ===");
            sb.AppendLine("Čas vygenerování: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            sb.AppendLine("Verze aplikace:   " + UpdateService.CurrentVersion + " (vydáno " + UpdateService.ReleaseDate + ")");
            sb.AppendLine("OS / .NET:        " + Environment.OSVersion + " | CLR " + Environment.Version);
            sb.AppendLine("Zvolený zdroj:    " + activeSource);

            int cacheFiles;
            long cacheBytes;
            DiskCache.GetCacheStats(out cacheFiles, out cacheBytes);
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Disková cache:    {0} souborů ({1:0.00} MB) v {2}",
                cacheFiles, cacheBytes / (1024.0 * 1024.0), DiskCache.GetCacheDirectoryPath()));
            sb.AppendLine("Soubor logu:      " + GetLogFilePath());
            sb.AppendLine();

            if (lastResult != null)
            {
                sb.AppendLine("--- Poslední načtená předpověď ---");
                sb.AppendLine("Staženo:          " + lastResult.FetchedAt.ToString("dd.MM.yyyy HH:mm:ss"));
                sb.AppendLine("Parametr:         " + CzechData.GetParameterInfo(lastResult.Parameter).DisplayName);
                sb.AppendLine("Skutečný zdroj:   " + (lastResult.DataSourceLabel ?? "N/A"));
                sb.AppendLine("Offline záloha:   " + (lastResult.IsOfflineFallback ? ("ANO (z " + lastResult.OfflineCachedAt.ToString("dd.MM.yyyy HH:mm") + ")") : "NE"));
                sb.AppendLine("Počet modelů:     " + lastResult.Models.Count);
                sb.AppendLine("Bodů sítě:        " + (lastResult.GridPoints != null ? lastResult.GridPoints.Count : 0));

                foreach (var m in lastResult.Models)
                {
                    string rt = "";
                    if (lastResult.ModelRunTimes != null && lastResult.ModelRunTimes.ContainsKey(m.Id))
                        rt = lastResult.ModelRunTimes[m.Id];
                    bool stale = EnsembleStats.IsModelRunStale(lastResult, m.Id);
                    string note = "";
                    if (lastResult.ModelNotes != null && lastResult.ModelNotes.ContainsKey(m.Id))
                        note = " [" + lastResult.ModelNotes[m.Id] + "]";
                    sb.AppendLine("  • " + m.DisplayName + ": běh " + (string.IsNullOrEmpty(rt) ? "neznámý" : rt) + (stale ? " [STARŠÍ >12h!]" : "") + note);
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("--- Předpověď dosud nebyla v této relaci stažena ---");
                sb.AppendLine();
            }

            sb.AppendLine("--- Poslední varování a chyby v relaci ---");
            lock (_lock)
            {
                if (_recentErrors.Count == 0)
                {
                    sb.AppendLine("(Žádné varování ani chyby)");
                }
                else
                {
                    foreach (var err in _recentErrors)
                        sb.AppendLine(err);
                }
            }

            return sb.ToString();
        }

        public static void ShowDiagnosticsDialog(Form owner, ForecastResult lastResult, DataSourcePreference activeSource)
        {
            string report = BuildDiagnosticReport(lastResult, activeSource);

            using (var dlg = new Form())
            {
                dlg.Text = "Diagnostika provozu — MeteoBarbucha ČR";
                dlg.Size = new Size(740, 520);
                dlg.MinimumSize = new Size(560, 380);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Font = new Font("Segoe UI", 9.5f);
                dlg.BackColor = Color.White;

                var txt = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Font = new Font("Consolas", 9.2f),
                    BackColor = Color.FromArgb(15, 23, 42),
                    ForeColor = Color.FromArgb(226, 232, 240),
                    Text = report
                };
                txt.SelectionStart = 0;
                txt.SelectionLength = 0;

                var bottom = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 50,
                    BackColor = Color.FromArgb(241, 245, 249),
                    Padding = new Padding(12, 8, 12, 8)
                };

                var btnCopy = new Button
                {
                    Text = "Kopírovat diagnostiku",
                    Location = new Point(12, 8),
                    Size = new Size(175, 32),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(37, 99, 235),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btnCopy.FlatAppearance.BorderSize = 0;
                btnCopy.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(txt.Text);
                        MessageBox.Show(dlg, "Diagnostický přehled byl zkopírován do schránky.", "Zkopírováno", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch { }
                };

                var btnOpenLog = new Button
                {
                    Text = "Otevřít složku logů",
                    Location = new Point(196, 8),
                    Size = new Size(155, 32),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(30, 41, 59),
                    Cursor = Cursors.Hand
                };
                btnOpenLog.Click += (s, e) =>
                {
                    try
                    {
                        string dir = GetLogDirectory();
                        Directory.CreateDirectory(dir);
                        Process.Start("explorer.exe", dir);
                    }
                    catch { }
                };

                var btnClose = new Button
                {
                    Text = "Zavřít",
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(dlg.ClientSize.Width - 112, 8),
                    Size = new Size(98, 32),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(51, 65, 85),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                btnClose.FlatAppearance.BorderSize = 0;
                btnClose.Click += (s, e) => dlg.Close();

                bottom.Controls.Add(btnCopy);
                bottom.Controls.Add(btnOpenLog);
                bottom.Controls.Add(btnClose);

                dlg.Controls.Add(txt);
                dlg.Controls.Add(bottom);
                dlg.ShowDialog(owner);
            }
        }
    }
}
