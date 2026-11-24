using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class SettingsDialog : Form
    {
        public int DataSourceIndex { get; private set; }
        public string OpenMeteoKeyOrUrl { get; private set; }
        public string MeteologixApiKey { get; private set; }
        public string OutputDir { get; private set; }
        public bool AutoRunEnabled { get; private set; }
        public int AutoHoursIndex { get; private set; }
        public bool CacheCleared { get; private set; }

        private readonly ComboBox _cmbDataSource;
        private readonly TextBox _txtOmKey;
        private readonly TextBox _txtMlxKey;
        private readonly Button _btnVerify;
        private readonly Label _lblVerifyStatus;
        private readonly TextBox _txtOutDir;
        private readonly CheckBox _chkAuto;
        private readonly ComboBox _cmbAutoHours;
        private readonly Label _lblCacheStats;
        private readonly ForecastResult _lastResult;

        public SettingsDialog(
            int dataSourceIndex,
            string openMeteoKeyOrUrl,
            string meteologixApiKey,
            string outputDir,
            bool autoRunEnabled,
            int autoHoursIndex,
            ForecastResult lastResult)
        {
            _lastResult = lastResult;

            Text = "Nastavení aplikace — MeteoBarbucha ČR";
            Size = new Size(560, 565);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;

            int y = 16;

            // Section 1: Data Sources & API Keys
            var lblSec1 = new Label
            {
                Text = "1. Zdroje dat a API klíče (uloženo šifrovaně přes Windows DPAPI)",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 26;

            var lblDs = new Label { Text = "Preferovaný režim zdrojů dat:", Location = new Point(20, y + 3), AutoSize = true };
            _cmbDataSource = new ComboBox
            {
                Location = new Point(225, y),
                Width = 300,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbDataSource.Items.AddRange(new object[]
            {
                "Automaticky (více zdrojů + záloha při 429)",
                "Alternativní zdroje (MET Norway + Bright Sky)",
                "Preferovat Meteologix API + Open-Meteo",
                "Pouze Open-Meteo API",
                "Pouze Meteologix / Kachelmann API"
            });
            _cmbDataSource.SelectedIndex = (dataSourceIndex >= 0 && dataSourceIndex < _cmbDataSource.Items.Count) ? dataSourceIndex : 0;
            y += 34;

            var lblOm = new Label { Text = "Open-Meteo klíč nebo vlastní URL (volitelné):", Location = new Point(20, y), AutoSize = true };
            y += 21;
            _txtOmKey = new TextBox
            {
                Location = new Point(20, y),
                Width = 505,
                Text = openMeteoKeyOrUrl ?? ""
            };
            y += 30;

            var lblMlx = new Label { Text = "Meteologix API klíč (X-API-Key, volitelné):", Location = new Point(20, y), AutoSize = true };
            y += 21;
            _txtMlxKey = new TextBox
            {
                Location = new Point(20, y),
                Width = 400,
                UseSystemPasswordChar = true,
                Text = meteologixApiKey ?? ""
            };
            _btnVerify = new Button
            {
                Text = "Ověřit klíč",
                Location = new Point(428, y - 1),
                Size = new Size(97, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnVerify.FlatAppearance.BorderSize = 0;
            _btnVerify.Click += async (s, e) =>
            {
                _btnVerify.Enabled = false;
                _lblVerifyStatus.Text = "Ověřuji Meteologix API klíč...";
                _lblVerifyStatus.ForeColor = Color.FromArgb(37, 99, 235);
                var res = await WeatherService.VerifyMeteologixApiKeyAsync(_txtMlxKey.Text);
                _lblVerifyStatus.Text = res.Item2;
                _lblVerifyStatus.ForeColor = res.Item1 ? Color.FromArgb(5, 150, 105) : Color.FromArgb(220, 38, 38);
                _btnVerify.Enabled = true;
            };
            y += 27;

            _lblVerifyStatus = new Label
            {
                Text = "Bez klíče aplikace využívá bezplatné zdroje Open-Meteo, MET Norway a Bright Sky.",
                Font = new Font("Segoe UI", 8.3f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, y),
                Size = new Size(505, 18)
            };
            y += 28;

            // Section 2: Output & Scheduled Download
            var sep1 = new Panel { Location = new Point(20, y), Size = new Size(505, 1), BackColor = Color.FromArgb(226, 232, 240) };
            y += 12;

            var lblSec2 = new Label
            {
                Text = "2. Ukládání výstupů a automatické stahování",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 24;

            var lblOut = new Label { Text = "Složka pro snímky map (PNG) a tabulky (CSV):", Location = new Point(20, y), AutoSize = true };
            y += 21;
            _txtOutDir = new TextBox
            {
                Location = new Point(20, y),
                Width = 400,
                Text = outputDir ?? ""
            };
            var btnBrowse = new Button
            {
                Text = "Vybrat...",
                Location = new Point(428, y - 1),
                Size = new Size(97, 26),
                FlatStyle = FlatStyle.Flat
            };
            btnBrowse.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog())
                {
                    fb.SelectedPath = _txtOutDir.Text;
                    if (fb.ShowDialog(this) == DialogResult.OK)
                        _txtOutDir.Text = fb.SelectedPath;
                }
            };
            y += 34;

            _chkAuto = new CheckBox
            {
                Text = "Automaticky stahovat čerstvá data pro vybrané modely každých:",
                Location = new Point(20, y + 3),
                AutoSize = true,
                Checked = autoRunEnabled
            };
            _cmbAutoHours = new ComboBox
            {
                Location = new Point(435, y),
                Width = 90,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbAutoHours.Items.AddRange(new object[] { "1 h", "3 h", "6 h", "12 h" });
            _cmbAutoHours.SelectedIndex = (autoHoursIndex >= 0 && autoHoursIndex < _cmbAutoHours.Items.Count) ? autoHoursIndex : 1;
            y += 26;

            var lblJitter = new Label
            {
                Text = "Časovač automaticky přidává náhodný posun (+1 až +12 min), aby se dotazy z více stanic nesešly v jeden okamžik.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(38, y),
                Size = new Size(488, 28)
            };
            y += 34;

            // Section 3: Cache & Diagnostics
            var sep2 = new Panel { Location = new Point(20, y), Size = new Size(505, 1), BackColor = Color.FromArgb(226, 232, 240) };
            y += 12;

            var lblSec3 = new Label
            {
                Text = "3. Mezipaměť (12h cache + offline záloha) a diagnostika",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 24;

            _lblCacheStats = new Label
            {
                Location = new Point(20, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(51, 65, 85)
            };
            RefreshCacheStatsLabel();
            y += 24;

            var btnClearCache = new Button
            {
                Text = "Vymazat mezipaměť (cache)",
                Location = new Point(20, y),
                Size = new Size(210, 31),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            btnClearCache.Click += (s, e) =>
            {
                int del = DiskCache.ClearAll();
                CacheCleared = true;
                RefreshCacheStatsLabel();
                MessageBox.Show(this, "Mezipaměť byla vymazána (" + del + " souborů). Další stažení načte čerstvá data ze sítě.", "Mezipaměť vymazána", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var btnDiag = new Button
            {
                Text = "Diagnostika a logy...",
                Location = new Point(242, y),
                Size = new Size(185, 31),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            btnDiag.Click += (s, e) =>
            {
                DataSourcePreference ds = MapIndexToSource(_cmbDataSource.SelectedIndex);
                AppLogger.ShowDiagnosticsDialog(this, _lastResult, ds);
            };

            // Bottom buttons
            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                BackColor = Color.FromArgb(241, 245, 249)
            };

            var btnSave = new Button
            {
                Text = "Uložit nastavení",
                Location = new Point(278, 10),
                Size = new Size(140, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                DataSourceIndex = _cmbDataSource.SelectedIndex;
                OpenMeteoKeyOrUrl = _txtOmKey.Text.Trim();
                MeteologixApiKey = _txtMlxKey.Text.Trim();
                OutputDir = _txtOutDir.Text.Trim();
                AutoRunEnabled = _chkAuto.Checked;
                AutoHoursIndex = _cmbAutoHours.SelectedIndex;
                DialogResult = DialogResult.OK;
                Close();
            };

            var btnCancel = new Button
            {
                Text = "Zrušit",
                Location = new Point(428, 10),
                Size = new Size(97, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(51, 65, 85),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCancel);

            Controls.AddRange(new Control[]
            {
                lblSec1, lblDs, _cmbDataSource, lblOm, _txtOmKey, lblMlx, _txtMlxKey, _btnVerify, _lblVerifyStatus,
                sep1, lblSec2, lblOut, _txtOutDir, btnBrowse, _chkAuto, _cmbAutoHours, lblJitter,
                sep2, lblSec3, _lblCacheStats, btnClearCache, btnDiag,
                bottom
            });
        }

        private void RefreshCacheStatsLabel()
        {
            int cnt;
            long bytes;
            DiskCache.GetCacheStats(out cnt, out bytes);
            _lblCacheStats.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Aktuálně uloženo v mezipaměti: {0} souborů ({1:0.00} MB)",
                cnt, bytes / (1024.0 * 1024.0));
        }

        public static DataSourcePreference MapIndexToSource(int index)
        {
            switch (index)
            {
                case 1: return DataSourcePreference.AlternativeOpenOnly;
                case 2: return DataSourcePreference.PreferMeteologix;
                case 3: return DataSourcePreference.OpenMeteoOnly;
                case 4: return DataSourcePreference.MeteologixOnly;
                default: return DataSourcePreference.AutoMultiSource;
            }
        }
    }
}
