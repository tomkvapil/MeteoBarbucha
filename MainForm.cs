using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class MainForm : Form
    {
        private readonly WeatherService _weatherService = new WeatherService();
        private readonly Random _jitterRng = new Random();
        private ForecastResult _lastResult;
        private readonly Dictionary<string, Dictionary<string, double?>> _modelMapsData = new Dictionary<string, Dictionary<string, double?>>();
        private readonly Dictionary<string, string> _modelMapKeys = new Dictionary<string, string>();
        private readonly Dictionary<string, ForecastResult> _resultCache = new Dictionary<string, ForecastResult>();
        private readonly List<string> _cacheOrder = new List<string>();
        private CancellationTokenSource _fetchCts;
        private bool _suppressAutoFetch;
        private bool _hasUserFetchedOnce;
        private Bitmap _currentPreviewBitmap;

        // Settings state (managed via SettingsDialog and persisted via SettingsManager)
        private int _dataSourceIndex = 0;
        private string _openMeteoKeyOrUrl = "";
        private string _meteologixApiKey = "";
        private string _outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeteoBarbucha");
        private bool _autoRunEnabled = false;
        private int _autoHoursIndex = 1;
        private string _updateManifestUrl = UpdateService.DefaultManifestUrl;

        // Sidebar operational controls
        private ComboBox _cmbParam;
        private ComboBox _cmbRollingWindow;
        private CheckBox _chkWarn;
        private NumericUpDown _numWarn;
        private Button _btnWarnPresets;
        private ComboBox _cmbAggMode;
        private RadioButton _rbDays;
        private RadioButton _rbHours;
        private ComboBox _cmbDays;
        private DateTimePicker _dtpStart;
        private DateTimePicker _dtpEnd;
        private Label _lblHourRange;
        private CheckedListBox _chkModels;
        private Button _btnRun;
        private Button _btnCancelFetch;
        private Button _btnSaveAll;
        private Label _lblSettingsSummary;

        // Timers
        private System.Windows.Forms.Timer _autoTimer;
        private System.Windows.Forms.Timer _ageTimer;
        private System.Windows.Forms.Timer _timelinePlayTimer;

        // Timeline & Tabs
        private TrackBar _trkTimeStep;
        private Label _lblTimeStep;
        private Button _btnPlayTimeline;
        private TabControl _tabs;
        private DataGridView _grid;

        // ORP Tab controls
        private TabPage _tabOrp;
        private DataGridView _gridOrp;
        private TextBox _txtOrpFilter;
        private CheckBox _chkOrpWarnOnly;

        // Maps & Area tabs
        private ComboBox _cmbMapSelect;
        private Label _lblSelectMap;
        private ZoomablePictureBox _picMap;
        private FlowLayoutPanel _multiViewPanel;
        private Button _btnBackToMultiView;
        private Button _btnResetZoom;
        private Label _lblZoomHint;
        private TabPage _tabArea;
        private FlowLayoutPanel _areaMultiViewPanel;
        private ComboBox _cmbAreaPreset;
        private readonly List<ZoomablePictureBox> _areaCards = new List<ZoomablePictureBox>();
        private float _areaZoom = 1.0f;
        private PointF _areaPan = PointF.Empty;
        private bool _syncingAreaView;
        private readonly List<Bitmap> _multiViewThumbnails = new List<Bitmap>();

        // Log & Status
        private TextBox _txtLog;
        private ToolStripStatusLabel _lblStatus;
        private ToolStripStatusLabel _lblDataAge;
        private ToolStripProgressBar _progress;

        public MainForm()
        {
            InitializeUi();
            ApplySavedSettings();
            UpdateAutoTimer();
            FormClosing += (s, e) =>
            {
                StopTimelinePlayback();
                SaveCurrentSettings();
            };
            Shown += async (s, e) => await CheckForUpdatesOnStartupAsync();
        }

        private void InitializeUi()
        {
            Text = "MeteoBarbucha ČR — Multi-Model Analyzátor";
            Size = new Size(1420, 840);
            MinimumSize = new Size(1340, 730);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(241, 245, 249);
            KeyPreview = true;

            // Top Header
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(18, 10, 18, 10)
            };

            var lblTitle = new Label
            {
                Text = "MeteoBarbucha ČR",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 10)
            };

            var lblCopyright = new Label
            {
                Text = "Made with Antigravity 2026",
                ForeColor = Color.FromArgb(96, 165, 250),
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                AutoSize = true,
                Location = new Point(228, 17)
            };

            var lblSub = new Label
            {
                Text = "Multi-modelová analýza předpovědí, pravděpodobnostní shody a výstrah pro 14 krajů a 206 ORP ČR",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(20, 40)
            };

            var btnSettings = CreateHeaderButton("⚙ Nastavení", header.Width - 875, 112, Color.FromArgb(37, 99, 235), Color.FromArgb(59, 130, 246), true);
            btnSettings.Click += (s, e) => OpenSettingsDialog();

            var btnOpenFolder = CreateHeaderButton("Složka výstupů", header.Width - 755, 122, Color.FromArgb(30, 41, 59), Color.FromArgb(71, 85, 105), false);
            btnOpenFolder.Click += (s, e) => OpenOutputFolder();

            var btnOpenWeb = CreateHeaderButton("Web Meteologix", header.Width - 625, 126, Color.FromArgb(30, 41, 59), Color.FromArgb(71, 85, 105), false);
            btnOpenWeb.Click += (s, e) =>
            {
                try { Process.Start("https://meteologix.com/cz/model-charts/euro/czech-republic/accumulated-precipitation"); }
                catch { }
            };

            var btnFeedback = CreateHeaderButton("Máte návrh na změnu?", header.Width - 491, 185, Color.FromArgb(30, 41, 59), Color.FromArgb(71, 85, 105), false);
            btnFeedback.Click += (s, e) =>
            {
                try { Process.Start("mailto:tom.kvapil89@gmail.com?subject=" + Uri.EscapeDataString("Návrh na změnu aplikace MeteoBarbucha ČR")); }
                catch { }
            };

            var btnHelp = CreateHeaderButton("Návod k použití", header.Width - 298, 125, Color.FromArgb(16, 185, 129), Color.FromArgb(5, 150, 105), true);
            btnHelp.Click += (s, e) =>
            {
                using (var dlg = new HelpDialog())
                    dlg.ShowDialog(this);
            };

            var btnUpdate = CreateHeaderButton("Aktualizace", header.Width - 165, 108, Color.FromArgb(51, 65, 85), Color.FromArgb(100, 116, 139), false);
            btnUpdate.Click += (s, e) => OpenUpdateDialog();

            var btnDiagTop = CreateHeaderButton("Diagnostika", header.Width - 49, 40, Color.FromArgb(51, 65, 85), Color.FromArgb(100, 116, 139), false);
            btnDiagTop.Text = "ℹ";
            btnDiagTop.Click += (s, e) => AppLogger.ShowDiagnosticsDialog(this, _lastResult, GetSelectedDataSource());

            header.Controls.AddRange(new Control[]
            {
                lblTitle, lblCopyright, lblSub,
                btnSettings, btnOpenFolder, btnOpenWeb, btnFeedback, btnHelp, btnUpdate, btnDiagTop
            });

            // Clean Left Sidebar (fits without scrolling!)
            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 328,
                BackColor = Color.White,
                Padding = new Padding(14),
                AutoScroll = true
            };

            int y = 10;

            var lblParam = new Label { Text = "Parametr předpovědi:", Font = new Font("Segoe UI", 9.2f, FontStyle.Bold), Location = new Point(14, y), AutoSize = true };
            y += 20;
            _cmbParam = new ComboBox
            {
                Location = new Point(14, y),
                Width = 294,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var p in CzechData.AvailableParameters)
                _cmbParam.Items.Add(p);
            _cmbParam.SelectedIndex = 0;

            var paramDebounceTimer = new System.Windows.Forms.Timer { Interval = 500 };
            paramDebounceTimer.Tick += async (s, e) =>
            {
                paramDebounceTimer.Stop();
                // Auto-download on parameter change ONLY after user has selected models and initiated at least one run!
                if (_hasUserFetchedOnce && (GetSelectedModels().Count > 0 || GetSelectedDataSource() == DataSourcePreference.AlternativeOpenOnly))
                    await RunFetchAndProcessAsync(true);
            };
            _cmbParam.SelectedIndexChanged += (s, e) =>
            {
                UpdateDefaultWarningForParam();
                if (!_suppressAutoFetch && _hasUserFetchedOnce)
                {
                    paramDebounceTimer.Stop();
                    paramDebounceTimer.Start();
                }
            };
            y += 28;

            var lblWin = new Label { Text = "Klouzavý úhrn / časové okno:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), Location = new Point(14, y), AutoSize = true };
            y += 19;
            _cmbRollingWindow = new ComboBox
            {
                Location = new Point(14, y),
                Width = 294,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbRollingWindow.Items.AddRange(new object[]
            {
                "Celé zvolené období (souhrn)",
                "Max. 1h intenzita / hodinový extrém",
                "Max. 3h klouzavý úhrn / extrém",
                "Max. 6h klouzavý úhrn / extrém",
                "Max. 12h klouzavý úhrn / extrém",
                "Max. 24h klouzavý úhrn / extrém"
            });
            _cmbRollingWindow.SelectedIndex = 0;
            _cmbRollingWindow.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressAutoFetch || _lastResult == null) return;
                EnsembleStats.ApplyRollingWindow(_lastResult, GetSelectedRollingHours());
                RefreshAllViewsFromLastResult();
            };
            y += 28;

            _chkWarn = new CheckBox
            {
                Text = "Výstraha ≥",
                Location = new Point(14, y + 3),
                AutoSize = true,
                Checked = false
            };
            _numWarn = new NumericUpDown
            {
                Location = new Point(108, y),
                Width = 72,
                Minimum = -50,
                Maximum = 5000,
                DecimalPlaces = 1,
                Value = 30.0m,
                Enabled = true
            };
            _btnWarnPresets = new Button
            {
                Text = "Prahy ČHMÚ ▾",
                Location = new Point(186, y - 1),
                Size = new Size(122, 25),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnWarnPresets.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            _btnWarnPresets.Click += (s, e) => ShowWarningPresetsMenu();

            _chkWarn.CheckedChanged += (s, e) => RefreshWarningOverlay();
            _numWarn.ValueChanged += (s, e) => RefreshWarningOverlay();
            y += 29;

            var lblAgg = new Label { Text = "Výpočet za kraj / ORP:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), Location = new Point(14, y), AutoSize = true };
            y += 19;
            _cmbAggMode = new ComboBox
            {
                Location = new Point(14, y),
                Width = 294,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbAggMode.Items.AddRange(new object[]
            {
                "Průměr v oblasti (ze sítě ČR)",
                "Maximum / extrém v oblasti (ze sítě ČR)",
                "Správní město (1 bod)"
            });
            _cmbAggMode.SelectedIndex = 0;
            _cmbAggMode.SelectedIndexChanged += (s, e) =>
            {
                if (_lastResult != null)
                    RefreshAllViewsFromLastResult();
            };
            y += 29;

            var lblHorizont = new Label { Text = "Období předpovědi:", Font = new Font("Segoe UI", 9f, FontStyle.Bold), Location = new Point(14, y), AutoSize = true };
            y += 21;

            _rbDays = new RadioButton
            {
                Text = "Horizont dopředu:",
                Location = new Point(14, y),
                AutoSize = true,
                Checked = true
            };
            _cmbDays = new ComboBox
            {
                Location = new Point(168, y - 2),
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbDays.Items.AddRange(new object[] { "1 den (24 h)", "2 dny (48 h)", "3 dny (72 h)", "5 dní (120 h)", "7 dní (168 h)" });
            _cmbDays.SelectedIndex = 2;
            y += 25;

            _rbHours = new RadioButton
            {
                Text = "Vlastní úsek v hodinách:",
                Location = new Point(14, y),
                AutoSize = true
            };
            y += 23;

            DateTime nowHour = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
            var lblOd = new Label { Text = "Od:", Location = new Point(28, y + 3), AutoSize = true };
            _dtpStart = new DateTimePicker
            {
                Location = new Point(62, y),
                Width = 246,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy HH:00",
                Value = nowHour,
                Enabled = false
            };
            y += 26;

            var lblDo = new Label { Text = "Do:", Location = new Point(28, y + 3), AutoSize = true };
            _dtpEnd = new DateTimePicker
            {
                Location = new Point(62, y),
                Width = 246,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy HH:00",
                Value = nowHour.AddHours(24),
                Enabled = false
            };
            y += 24;

            _lblHourRange = new Label
            {
                Text = "Režim: od aktuální hodiny dopředu",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.1f),
                Location = new Point(62, y),
                AutoSize = true
            };
            y += 21;

            _rbDays.CheckedChanged += (s, e) => UpdatePeriodModeUi();
            _rbHours.CheckedChanged += (s, e) => UpdatePeriodModeUi();
            _dtpStart.ValueChanged += (s, e) => UpdateHourRangeLabel();
            _dtpEnd.ValueChanged += (s, e) => UpdateHourRangeLabel();

            var lblModels = new Label { Text = "Meteorologické modely:", Font = new Font("Segoe UI", 9.2f, FontStyle.Bold), Location = new Point(14, y), AutoSize = true };
            y += 20;

            // Preset links row for quick model group selection
            var lnkHiRes = new LinkLabel { Text = "Hi-Res", Location = new Point(14, y), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            var lnkGlobal = new LinkLabel { Text = "Globální", Location = new Point(64, y), AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            var lnkAlt = new LinkLabel { Text = "Alt. zdroje", Location = new Point(122, y), AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            var lnkAll = new LinkLabel { Text = "Vše", Location = new Point(202, y), AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            var lnkNone = new LinkLabel { Text = "Nic", Location = new Point(236, y), AutoSize = true, Font = new Font("Segoe UI", 8.5f) };

            lnkHiRes.Click += (s, e) => SelectModelPreset("hires");
            lnkGlobal.Click += (s, e) => SelectModelPreset("global");
            lnkAlt.Click += (s, e) => SelectModelPreset("alt");
            lnkAll.Click += (s, e) => SetAllModelsChecked(true);
            lnkNone.Click += (s, e) => SetAllModelsChecked(false);
            y += 19;

            _chkModels = new CheckedListBox
            {
                Location = new Point(14, y),
                Size = new Size(294, 178),
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            // IMPORTANT: Zero models selected on startup to protect open APIs!
            foreach (var m in CzechData.AvailableModels)
                _chkModels.Items.Add(m, false);
            y += 186;

            _btnRun = new Button
            {
                Text = "Stáhnout data a spočítat",
                Location = new Point(14, y),
                Size = new Size(294, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRun.FlatAppearance.BorderSize = 0;
            _btnRun.Click += async (s, e) =>
            {
                _hasUserFetchedOnce = true;
                await RunFetchAndProcessAsync(false);
            };

            _btnCancelFetch = new Button
            {
                Text = "✕ Zrušit",
                Location = new Point(224, y),
                Size = new Size(84, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Visible = false,
                Cursor = Cursors.Hand
            };
            _btnCancelFetch.FlatAppearance.BorderSize = 0;
            _btnCancelFetch.Click += (s, e) =>
            {
                if (_fetchCts != null)
                    _fetchCts.Cancel();
            };
            y += 43;

            _btnSaveAll = new Button
            {
                Text = "Uložit snímky a tabulky (CSV)",
                Location = new Point(14, y),
                Size = new Size(294, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.3f, FontStyle.Bold),
                Enabled = false,
                Cursor = Cursors.Hand
            };
            _btnSaveAll.FlatAppearance.BorderSize = 0;
            _btnSaveAll.Click += (s, e) => SaveAllResults(true);
            y += 40;

            var lnkSettingsSidebar = new LinkLabel
            {
                Text = "⚙ Zdroj dat, API klíče, cache a automatika...",
                Location = new Point(14, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.6f, FontStyle.Bold)
            };
            lnkSettingsSidebar.Click += (s, e) => OpenSettingsDialog();
            y += 19;

            _lblSettingsSummary = new Label
            {
                Text = "Zdroj: Automaticky (více zdrojů)",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.1f),
                Location = new Point(14, y),
                Size = new Size(294, 30)
            };

            sidebar.Controls.AddRange(new Control[]
            {
                lblParam, _cmbParam,
                lblWin, _cmbRollingWindow,
                _chkWarn, _numWarn, _btnWarnPresets,
                lblAgg, _cmbAggMode,
                lblHorizont, _rbDays, _cmbDays, _rbHours, lblOd, _dtpStart, lblDo, _dtpEnd, _lblHourRange,
                lblModels, lnkHiRes, lnkGlobal, lnkAlt, lnkAll, lnkNone, _chkModels,
                _btnCancelFetch, _btnRun, _btnSaveAll, lnkSettingsSidebar, _lblSettingsSummary
            });

            // Background Timers
            _autoTimer = new System.Windows.Forms.Timer();
            _autoTimer.Tick += async (s, e) =>
            {
                // Reschedule next tick with fresh random jitter (+1..12 min)
                ScheduleNextJitteredAutoTick(false);
                if (GetSelectedModels().Count == 0 && GetSelectedDataSource() != DataSourcePreference.AlternativeOpenOnly)
                {
                    AppendLog("Automatické stahování přeskočeno (nejsou vybrány žádné modely).");
                    return;
                }
                AppendLog("Spouštím automatické stažení dat na pozadí...");
                await RunFetchAndProcessAsync(false);
            };

            _ageTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _ageTimer.Tick += (s, e) => UpdateDataAgeStatus();
            _ageTimer.Start();

            // Main Workspace Panel
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            var timelineBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.White,
                Padding = new Padding(10, 4, 10, 4)
            };
            var lblTimeTitle = new Label
            {
                Text = "Časová osa:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(10, 10),
                AutoSize = true
            };
            _btnPlayTimeline = new Button
            {
                Text = "▶ Přehrát",
                Location = new Point(92, 5),
                Size = new Size(86, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                Enabled = false,
                Cursor = Cursors.Hand
            };
            _btnPlayTimeline.FlatAppearance.BorderSize = 0;
            _btnPlayTimeline.Click += (s, e) => ToggleTimelinePlayback();

            _timelinePlayTimer = new System.Windows.Forms.Timer { Interval = 900 };
            _timelinePlayTimer.Tick += (s, e) => AdvanceTimelineStep();

            _trkTimeStep = new TrackBar
            {
                Location = new Point(184, 3),
                Width = 370,
                Minimum = -1,
                Maximum = -1,
                Value = -1,
                TickStyle = TickStyle.None,
                Enabled = false
            };
            _lblTimeStep = new Label
            {
                Text = "Celé období (souhrn)   |   Zkratky: Mezerník (přehrát), ← / → (krok)",
                ForeColor = Color.FromArgb(37, 99, 235),
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                Location = new Point(562, 10),
                AutoSize = true
            };
            _trkTimeStep.ValueChanged += (s, e) => OnTimeStepChanged();
            timelineBar.Controls.AddRange(new Control[] { lblTimeTitle, _btnPlayTimeline, _trkTimeStep, _lblTimeStep });

            _tabs = new TabControl { Dock = DockStyle.Fill };

            // Tab 1: Kraje Table
            var tabTable = new TabPage("Tabulka krajů (14 krajů)");
            _grid = CreateStyledGrid(true);
            tabTable.Controls.Add(_grid);

            // Tab 2: ORP Table (206 ORP municipalities)
            _tabOrp = new TabPage("Tabulka ORP (206 obcí)");
            var orpTop = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.White, Padding = new Padding(8) };
            var lblOrpSearch = new Label { Text = "Hledat ORP / kraj:", Location = new Point(10, 12), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _txtOrpFilter = new TextBox { Location = new Point(134, 9), Width = 190 };
            _txtOrpFilter.TextChanged += (s, e) => { if (_lastResult != null) PopulateOrpGrid(_lastResult); };

            _chkOrpWarnOnly = new CheckBox
            {
                Text = "Jen ORP nad prahem shody (≥ 50 % modelů)",
                Location = new Point(340, 11),
                AutoSize = true
            };
            _chkOrpWarnOnly.CheckedChanged += (s, e) => { if (_lastResult != null) PopulateOrpGrid(_lastResult); };

            var btnExportOrp = new Button
            {
                Text = "Exportovat ORP do CSV",
                Location = new Point(645, 7),
                Size = new Size(165, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnExportOrp.FlatAppearance.BorderSize = 0;
            btnExportOrp.Click += (s, e) => ExportOrpCsvOnly();

            var lblOrpHint = new Label
            {
                Text = "Dvojklik na řádek ORP otevře meteogram / sondáž",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.3f),
                Location = new Point(822, 13),
                AutoSize = true
            };

            orpTop.Controls.AddRange(new Control[] { lblOrpSearch, _txtOrpFilter, _chkOrpWarnOnly, btnExportOrp, lblOrpHint });

            _gridOrp = CreateStyledGrid(false);
            _gridOrp.CellDoubleClick += OnOrpRowDoubleClicked;
            _tabOrp.Controls.Add(_gridOrp);
            _tabOrp.Controls.Add(orpTop);

            // Tab 3: Maps (Multi-view + Detail)
            var tabMaps = new TabPage("Mapy ČR (Multi-view / Detail)");
            var mapTop = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), BackColor = Color.White };

            _btnBackToMultiView = new Button
            {
                Text = "← Zpět na Multi-view",
                Location = new Point(10, 9),
                Size = new Size(155, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Visible = false,
                Cursor = Cursors.Hand
            };
            _btnBackToMultiView.FlatAppearance.BorderSize = 0;
            _btnBackToMultiView.Click += (s, e) => SwitchToMultiView();

            _lblSelectMap = new Label { Text = "Detail mapy:", Location = new Point(175, 13), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Enabled = false };
            _cmbMapSelect = new ComboBox { Location = new Point(262, 10), Width = 280, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            _cmbMapSelect.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbMapSelect.SelectedItem != null && _picMap.Visible)
                    DisplaySelectedMap();
            };

            _btnResetZoom = new Button
            {
                Text = "Resetovat zoom",
                Location = new Point(552, 9),
                Size = new Size(118, 28),
                FlatStyle = FlatStyle.Flat,
                Visible = false
            };
            _lblZoomHint = new Label
            {
                Text = "Klikněte na libovolnou mapu v Multi-view pro detail a zoom",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(680, 14),
                AutoSize = true
            };

            mapTop.Controls.AddRange(new Control[] { _btnBackToMultiView, _lblSelectMap, _cmbMapSelect, _btnResetZoom, _lblZoomHint });

            _picMap = new ZoomablePictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 248, 252),
                Visible = false
            };
            _picMap.ImageMouseMoved += OnMapMouseMoved;
            _picMap.ImageClicked += pt => OpenMeteogramAtPixel(pt, _cmbMapSelect.SelectedItem != null ? _cmbMapSelect.SelectedItem.ToString() : null);
            _btnResetZoom.Click += (s, e) => _picMap.ResetView();

            _multiViewPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(10),
                Visible = true
            };

            tabMaps.Controls.Add(_picMap);
            tabMaps.Controls.Add(_multiViewPanel);
            tabMaps.Controls.Add(mapTop);

            // Tab 4: Výběr oblast
            _tabArea = new TabPage("Výběr oblast");
            var areaTop = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), BackColor = Color.White };
            var lblArea = new Label { Text = "Oblast zájmu:", Location = new Point(10, 13), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _cmbAreaPreset = new ComboBox { Location = new Point(100, 10), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbAreaPreset.Items.AddRange(new object[]
            {
                "Celá ČR (výchozí)",
                "Praha a Střední Čechy",
                "Šumava / Jižní Čechy",
                "Západní Čechy (PLK / KVK)",
                "Krušné hory / Ústecko",
                "Krkonoše / Liberecko",
                "Východní Čechy (HKK / PAK)",
                "Kraj Vysočina",
                "Jižní Morava",
                "Jeseníky / Olomoucko",
                "Beskydy / Ostravsko"
            });
            _cmbAreaPreset.SelectedIndex = 0;
            _cmbAreaPreset.SelectedIndexChanged += (s, e) => ApplyAreaPreset(_cmbAreaPreset.SelectedIndex);

            var btnZoomIn = new Button
            {
                Text = "Lupa +",
                Location = new Point(332, 9),
                Size = new Size(76, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnZoomIn.FlatAppearance.BorderSize = 0;
            btnZoomIn.Click += (s, e) => ZoomAllAreaCards(1.25f);

            var btnZoomOut = new Button
            {
                Text = "Lupa −",
                Location = new Point(414, 9),
                Size = new Size(76, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnZoomOut.FlatAppearance.BorderSize = 0;
            btnZoomOut.Click += (s, e) => ZoomAllAreaCards(1f / 1.25f);

            var btnAreaReset = new Button
            {
                Text = "Celá ČR (0)",
                Location = new Point(496, 9),
                Size = new Size(92, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnAreaReset.Click += (s, e) =>
            {
                _cmbAreaPreset.SelectedIndex = 0;
                ApplyAreaPreset(0);
            };

            var lblAreaHint = new Label
            {
                Text = "Zkratky: + / − (lupa), šipky (posun), 0 (reset), Alt+←/→ (časová osa)",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(598, 14),
                AutoSize = true
            };

            areaTop.Controls.AddRange(new Control[] { lblArea, _cmbAreaPreset, btnZoomIn, btnZoomOut, btnAreaReset, lblAreaHint });

            _areaMultiViewPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(10)
            };

            _tabArea.Controls.Add(_areaMultiViewPanel);
            _tabArea.Controls.Add(areaTop);

            // Tab 5: Log & Diagnostics
            var tabLog = new TabPage("Log průběhu");
            var logTop = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.White, Padding = new Padding(8, 6, 8, 6) };
            var btnLogDiag = new Button
            {
                Text = "Diagnostika provozu...",
                Location = new Point(8, 6),
                Size = new Size(168, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnLogDiag.FlatAppearance.BorderSize = 0;
            btnLogDiag.Click += (s, e) => AppLogger.ShowDiagnosticsDialog(this, _lastResult, GetSelectedDataSource());

            var btnLogFolder = new Button
            {
                Text = "Otevřít složku logů",
                Location = new Point(184, 6),
                Size = new Size(148, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnLogFolder.Click += (s, e) =>
            {
                try
                {
                    string dir = AppLogger.GetLogDirectory();
                    Directory.CreateDirectory(dir);
                    Process.Start("explorer.exe", dir);
                }
                catch { }
            };
            logTop.Controls.Add(btnLogDiag);
            logTop.Controls.Add(btnLogFolder);

            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new Font("Consolas", 9.5f),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            tabLog.Controls.Add(_txtLog);
            tabLog.Controls.Add(logTop);

            _tabs.TabPages.Add(tabTable);
            _tabs.TabPages.Add(_tabOrp);
            _tabs.TabPages.Add(tabMaps);
            _tabs.TabPages.Add(_tabArea);
            _tabs.TabPages.Add(tabLog);

            mainPanel.Controls.Add(_tabs);
            mainPanel.Controls.Add(timelineBar);

            // StatusStrip
            var statusStrip = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel("Připraveno. Vyberte meteorologické modely vlevo a klikněte na 'Stáhnout data a spočítat'.")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _progress = new ToolStripProgressBar { Width = 150, Style = ProgressBarStyle.Blocks, Visible = false };
            _lblDataAge = new ToolStripStatusLabel("Data: dosud nenačtena")
            {
                ForeColor = Color.FromArgb(71, 85, 105),
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                Padding = new Padding(8, 0, 8, 0)
            };
            var lblVersion = new ToolStripStatusLabel("Verze " + UpdateService.CurrentVersion + "  |  Vydáno: " + UpdateService.ReleaseDate)
            {
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85),
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                Padding = new Padding(10, 0, 6, 0)
            };

            statusStrip.Items.Add(_lblStatus);
            statusStrip.Items.Add(_progress);
            statusStrip.Items.Add(_lblDataAge);
            statusStrip.Items.Add(lblVersion);

            Controls.Add(mainPanel);
            Controls.Add(sidebar);
            Controls.Add(header);
            Controls.Add(statusStrip);

            UpdateSettingsSummaryLabel();
        }

        private static Button CreateHeaderButton(string text, int x, int width, Color bg, Color border, bool bold)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(width, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(x, 17),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            if (bold)
                btn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btn.FlatAppearance.BorderColor = border;
            return btn;
        }

        private static DataGridView CreateStyledGrid(bool autoFill)
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = autoFill ? DataGridViewAutoSizeColumnsMode.Fill : DataGridViewAutoSizeColumnsMode.DisplayedCells,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = 42
            };
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.8f, FontStyle.Bold);
            return g;
        }

        private void OpenSettingsDialog()
        {
            using (var dlg = new SettingsDialog(
                _dataSourceIndex,
                _openMeteoKeyOrUrl,
                _meteologixApiKey,
                _outputDir,
                _autoRunEnabled,
                _autoHoursIndex,
                _lastResult))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _dataSourceIndex = dlg.DataSourceIndex;
                    _openMeteoKeyOrUrl = dlg.OpenMeteoKeyOrUrl;
                    _meteologixApiKey = dlg.MeteologixApiKey;
                    _outputDir = dlg.OutputDir;
                    _autoRunEnabled = dlg.AutoRunEnabled;
                    _autoHoursIndex = dlg.AutoHoursIndex;

                    if (dlg.CacheCleared)
                    {
                        _resultCache.Clear();
                        _cacheOrder.Clear();
                    }

                    UpdateSettingsSummaryLabel();
                    UpdateAutoTimer();
                    SaveCurrentSettings();
                }
                else if (dlg.CacheCleared)
                {
                    _resultCache.Clear();
                    _cacheOrder.Clear();
                }
            }
        }

        private void UpdateSettingsSummaryLabel()
        {
            string dsName;
            switch (_dataSourceIndex)
            {
                case 1: dsName = "MET Norway + Bright Sky"; break;
                case 2: dsName = "Meteologix + Open-Meteo"; break;
                case 3: dsName = "Pouze Open-Meteo"; break;
                case 4: dsName = "Pouze Meteologix"; break;
                default: dsName = "Automaticky (více zdrojů)"; break;
            }
            string autoPart = _autoRunEnabled ? " | Auto: zapnuto" : "";
            _lblSettingsSummary.Text = "Zdroj: " + dsName + autoPart;
        }

        private void ShowWarningPresetsMenu()
        {
            var menu = new ContextMenuStrip();
            var presets = CzechData.GetWarningPresets(GetSelectedParameter());
            foreach (var p in presets)
            {
                var preset = p;
                var item = menu.Items.Add(preset.Label);
                item.Click += (s, e) =>
                {
                    _suppressAutoFetch = true;
                    try
                    {
                        _numWarn.Value = Math.Max(_numWarn.Minimum, Math.Min(_numWarn.Maximum, (decimal)preset.Threshold));
                        _chkWarn.Checked = true;
                    }
                    finally
                    {
                        _suppressAutoFetch = false;
                    }
                    RefreshWarningOverlay();
                };
            }
            menu.Show(_btnWarnPresets, new Point(0, _btnWarnPresets.Height));
        }

        private void SelectModelPreset(string group)
        {
            for (int i = 0; i < _chkModels.Items.Count; i++)
            {
                var m = _chkModels.Items[i] as WeatherModelInfo;
                if (m == null) continue;
                bool check = false;
                if (group == "hires") check = CzechData.IsHighResModel(m.Id);
                else if (group == "global") check = CzechData.IsGlobalModel(m.Id);
                else if (group == "alt") check = !CzechData.IsOpenMeteoModel(m.Id);
                _chkModels.SetItemChecked(i, check);
            }
        }

        private int GetSelectedRollingHours()
        {
            switch (_cmbRollingWindow.SelectedIndex)
            {
                case 1: return 1;
                case 2: return 3;
                case 3: return 6;
                case 4: return 12;
                case 5: return 24;
                default: return 0;
            }
        }

        private void UpdatePeriodModeUi()
        {
            bool daysMode = _rbDays.Checked;
            _cmbDays.Enabled = daysMode;
            _dtpStart.Enabled = !daysMode;
            _dtpEnd.Enabled = !daysMode;
            UpdateHourRangeLabel();
        }

        private void UpdateHourRangeLabel()
        {
            if (_rbDays.Checked)
            {
                _lblHourRange.Text = "Režim: od aktuální hodiny dopředu";
                _lblHourRange.ForeColor = Color.FromArgb(100, 116, 139);
                return;
            }

            var s = new DateTime(_dtpStart.Value.Year, _dtpStart.Value.Month, _dtpStart.Value.Day, _dtpStart.Value.Hour, 0, 0);
            var e = new DateTime(_dtpEnd.Value.Year, _dtpEnd.Value.Month, _dtpEnd.Value.Day, _dtpEnd.Value.Hour, 0, 0);
            double hours = (e - s).TotalHours;
            if (hours <= 0)
            {
                _lblHourRange.Text = "Chyba: 'Do' musí být po 'Od'";
                _lblHourRange.ForeColor = Color.FromArgb(220, 38, 38);
            }
            else
            {
                _lblHourRange.Text = "Rozsah: " + hours.ToString("0", CultureInfo.InvariantCulture) + " hodin";
                _lblHourRange.ForeColor = Color.FromArgb(37, 99, 235);
            }
        }

        private void SetAllModelsChecked(bool state)
        {
            for (int i = 0; i < _chkModels.Items.Count; i++)
                _chkModels.SetItemChecked(i, state);
        }

        private void OpenOutputFolder()
        {
            string dir = (_outputDir ?? "").Trim();
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        }

        private int GetSelectedDays()
        {
            switch (_cmbDays.SelectedIndex)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 3;
                case 3: return 5;
                case 4: return 7;
                default: return 3;
            }
        }

        private List<WeatherModelInfo> GetSelectedModels()
        {
            var list = new List<WeatherModelInfo>();
            foreach (var item in _chkModels.CheckedItems)
            {
                var m = item as WeatherModelInfo;
                if (m != null) list.Add(m);
            }
            return list;
        }

        private void AppendLog(string message)
        {
            AppLogger.Info(message);
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLogUiOnly), message);
                return;
            }
            AppendLogUiOnly(message);
        }

        private void AppendLogUiOnly(string message)
        {
            if (_txtLog != null && !_txtLog.IsDisposed)
                _txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }

        private void ReportDownloadProgress(int current, int total, string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, int, string>(ReportDownloadProgress), current, total, text);
                return;
            }
            if (total > 0)
            {
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.Maximum = total;
                _progress.Value = Math.Max(0, Math.Min(total, current));
            }
            if (!string.IsNullOrEmpty(text))
                _lblStatus.Text = text;
        }

        private WeatherParameter GetSelectedParameter()
        {
            var info = _cmbParam.SelectedItem as WeatherParameterInfo;
            return info != null ? info.Parameter : WeatherParameter.TotalPrecipitation;
        }

        private RegionAggregationMode GetSelectedAggMode()
        {
            if (_cmbAggMode.SelectedIndex == 1) return RegionAggregationMode.RegionMax;
            if (_cmbAggMode.SelectedIndex == 2) return RegionAggregationMode.PointValue;
            return RegionAggregationMode.RegionAverage;
        }

        private void UpdateAutoTimer()
        {
            _autoTimer.Stop();
            if (!_autoRunEnabled)
                return;
            ScheduleNextJitteredAutoTick(true);
        }

        private void ScheduleNextJitteredAutoTick(bool logActivation)
        {
            _autoTimer.Stop();
            if (!_autoRunEnabled) return;

            int baseHours = 3;
            switch (_autoHoursIndex)
            {
                case 0: baseHours = 1; break;
                case 1: baseHours = 3; break;
                case 2: baseHours = 6; break;
                case 3: baseHours = 12; break;
            }

            // Add +1 to +12 minutes of random jitter to spread institutional requests across time
            int jitterSeconds = _jitterRng.Next(60, 721);
            int totalMs = (baseHours * 3600 + jitterSeconds) * 1000;
            _autoTimer.Interval = totalMs;
            _autoTimer.Start();

            DateTime nextAt = DateTime.Now.AddMilliseconds(totalMs);
            if (logActivation)
            {
                AppendLog(string.Format(
                    CultureInfo.InvariantCulture,
                    "Automatické stahování zapnuto (interval {0} h + rozložení +{1:0.0} min, další v {2:HH:mm:ss}).",
                    baseHours, jitterSeconds / 60.0, nextAt));
            }
        }

        private void UpdateDataAgeStatus()
        {
            if (_lastResult == null)
            {
                _lblDataAge.Text = "Data: dosud nenačtena";
                _lblDataAge.ForeColor = Color.FromArgb(100, 116, 139);
                return;
            }

            if (_lastResult.IsOfflineFallback)
            {
                _lblDataAge.Text = "⚠ OFFLINE záloha (" + _lastResult.OfflineCachedAt.ToString("dd.MM. HH:mm") + ")";
                _lblDataAge.ForeColor = Color.FromArgb(180, 83, 9);
                return;
            }

            int mins = Math.Max(0, (int)Math.Round((DateTime.Now - _lastResult.FetchedAt).TotalMinutes));
            string agePart = mins == 0 ? "právě teď" : ("před " + mins + " min");
            _lblDataAge.Text = "Staženo: " + agePart + " (" + _lastResult.FetchedAt.ToString("HH:mm") + ")";
            _lblDataAge.ForeColor = mins > 180 ? Color.FromArgb(180, 83, 9) : Color.FromArgb(51, 65, 85);
        }

        private DataSourcePreference GetSelectedDataSource()
        {
            return SettingsDialog.MapIndexToSource(_dataSourceIndex);
        }

        private void ToggleTimelinePlayback()
        {
            if (_timelinePlayTimer.Enabled)
            {
                StopTimelinePlayback();
                return;
            }
            if (!_trkTimeStep.Enabled || _trkTimeStep.Maximum < 0)
                return;

            if (_trkTimeStep.Value < 0 || _trkTimeStep.Value >= _trkTimeStep.Maximum)
                _trkTimeStep.Value = 0;

            _btnPlayTimeline.Text = "⏸ Pauza";
            _btnPlayTimeline.BackColor = Color.FromArgb(217, 119, 6);
            _timelinePlayTimer.Start();
        }

        private void StopTimelinePlayback()
        {
            if (_timelinePlayTimer != null)
                _timelinePlayTimer.Stop();
            if (_btnPlayTimeline != null)
            {
                _btnPlayTimeline.Text = "▶ Přehrát";
                _btnPlayTimeline.BackColor = Color.FromArgb(37, 99, 235);
            }
        }

        private void AdvanceTimelineStep()
        {
            if (_lastResult == null || !_trkTimeStep.Enabled || _trkTimeStep.Maximum < 0)
            {
                StopTimelinePlayback();
                return;
            }

            int next = _trkTimeStep.Value + 1;
            if (next < 0 || next > _trkTimeStep.Maximum)
                next = 0;
            _trkTimeStep.Value = next;
        }

        private string BuildCacheKey(List<WeatherModelInfo> models, int days, DateTime? startHour, DateTime? endHour, WeatherParameter param, DataSourcePreference ds)
        {
            var ids = new List<string>();
            foreach (var m in models) ids.Add(m.Id);
            string period = (startHour.HasValue && endHour.HasValue)
                ? startHour.Value.ToString("yyyyMMddHH") + "-" + endHour.Value.ToString("yyyyMMddHH")
                : "D" + days + "@" + DateTime.Now.ToString("yyyyMMddHH");
            return ds + "|" + param + "|" + period + "|" + string.Join(",", ids) + "|" + (_meteologixApiKey ?? "").Trim() + "|" + (_openMeteoKeyOrUrl ?? "").Trim();
        }

        private void UpdateDefaultWarningForParam()
        {
            var p = GetSelectedParameter();
            _suppressAutoFetch = true;
            try
            {
                _chkWarn.Text = EnsembleStats.IsBelowThresholdWarning(p) ? "Výstraha ≤" : "Výstraha ≥";

                switch (p)
                {
                    case WeatherParameter.UtciMax:
                        _chkWarn.Checked = true;
                        _numWarn.Value = 32.0m;
                        break;
                    case WeatherParameter.WindGusts10m:
                    case WeatherParameter.WindSpeed10m:
                        _numWarn.Value = 65.0m;
                        break;
                    case WeatherParameter.TotalPrecipitation:
                        _numWarn.Value = 30.0m;
                        break;
                    case WeatherParameter.Snowfall:
                        _numWarn.Value = 7.0m;
                        break;
                    case WeatherParameter.Temperature2mMax:
                        _numWarn.Value = 31.0m;
                        break;
                    case WeatherParameter.Temperature2mMin:
                        _numWarn.Value = -12.0m;
                        break;
                    case WeatherParameter.Cape:
                        _numWarn.Value = 1000.0m;
                        break;
                    case WeatherParameter.FreezingLevelHeight:
                        _numWarn.Value = 1000.0m;
                        break;
                    case WeatherParameter.DewPoint2m:
                        _numWarn.Value = 16.0m;
                        break;
                    default:
                        _numWarn.Value = 30.0m;
                        break;
                }
            }
            finally
            {
                _suppressAutoFetch = false;
            }
        }

        private void RefreshWarningOverlay()
        {
            if (_suppressAutoFetch || _lastResult == null) return;
            _lastResult.EffectiveAgreementThreshold = (double)_numWarn.Value;
            _lastResult.WarningThreshold = _chkWarn.Checked ? (double?)_numWarn.Value : null;
            RefreshAllViewsFromLastResult();
        }

        private void RefreshAllViewsFromLastResult()
        {
            if (_lastResult == null) return;
            _lastResult.EffectiveAgreementThreshold = (double)_numWarn.Value;
            _lastResult.WarningThreshold = _chkWarn.Checked ? (double?)_numWarn.Value : null;
            PopulateGrid(_lastResult);
            PopulateOrpGrid(_lastResult);
            BuildSummaryMaps(_lastResult);
            if (_picMap.Visible)
                DisplaySelectedMap();
        }

        private void SetupTimelineForResult(ForecastResult result)
        {
            StopTimelinePlayback();
            _suppressAutoFetch = true;
            try
            {
                if (result != null && result.TimeSteps != null && result.TimeSteps.Count > 1)
                {
                    _trkTimeStep.Minimum = -1;
                    _trkTimeStep.Maximum = result.TimeSteps.Count - 1;
                    _trkTimeStep.Value = Math.Max(-1, Math.Min(result.TimeSteps.Count - 1, result.SelectedTimeStepIndex));
                    _trkTimeStep.Enabled = true;
                    _btnPlayTimeline.Enabled = true;
                }
                else
                {
                    _trkTimeStep.Minimum = -1;
                    _trkTimeStep.Maximum = -1;
                    _trkTimeStep.Value = -1;
                    _trkTimeStep.Enabled = false;
                    _btnPlayTimeline.Enabled = false;
                }
                UpdateTimeStepLabel();
            }
            finally
            {
                _suppressAutoFetch = false;
            }
        }

        private void UpdateTimeStepLabel()
        {
            if (_lastResult == null || _trkTimeStep.Value < 0 || _lastResult.TimeSteps == null || _trkTimeStep.Value >= _lastResult.TimeSteps.Count)
            {
                string winNote = (_lastResult != null && _lastResult.RollingWindowHours > 0)
                    ? "Max. " + _lastResult.RollingWindowHours + "h klouzavé okno"
                    : "Celé období (souhrn)";
                _lblTimeStep.Text = winNote + "   |   Mezerník: přehrát, ←/→: krok";
            }
            else
            {
                _lblTimeStep.Text = "Krok: " + _lastResult.TimeSteps[_trkTimeStep.Value].Replace("T", " ");
            }
        }

        private void OnTimeStepChanged()
        {
            if (_suppressAutoFetch || _lastResult == null) return;
            _lastResult.SelectedTimeStepIndex = _trkTimeStep.Value;
            UpdateTimeStepLabel();
            RefreshAllViewsFromLastResult();
        }

        private async System.Threading.Tasks.Task RunFetchAndProcessAsync(bool useCache = false)
        {
            StopTimelinePlayback();
            var ds = GetSelectedDataSource();
            var models = GetSelectedModels();
            if (models.Count == 0 && ds != DataSourcePreference.AlternativeOpenOnly && ds != DataSourcePreference.MeteologixOnly)
            {
                MessageBox.Show(this, "Nejprve v levém panelu zaškrtněte alespoň jeden meteorologický model (nebo klikněte na předvolbu Hi-Res / Globální / Vše).", "Vyberte modely", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_fetchCts != null)
            {
                _fetchCts.Cancel();
                _fetchCts.Dispose();
            }
            _fetchCts = new CancellationTokenSource();
            var ct = _fetchCts.Token;

            _btnRun.Width = 204;
            _btnCancelFetch.Visible = true;
            _btnCancelFetch.BringToFront();
            _btnSaveAll.Enabled = false;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            _lblStatus.Text = "Stahuji data a počítám hodnoty...";

            try
            {
                int days = GetSelectedDays();
                DateTime? startHour = _rbHours.Checked ? (DateTime?)_dtpStart.Value : null;
                DateTime? endHour = _rbHours.Checked ? (DateTime?)_dtpEnd.Value : null;
                WeatherParameter param = GetSelectedParameter();

                string cacheKey = BuildCacheKey(models, days, startHour, endHour, param, ds);
                ForecastResult cached;
                if (useCache && _resultCache.TryGetValue(cacheKey, out cached))
                {
                    _lastResult = cached;
                    AppendLog("Načteno z mezipaměti (bez čekání): " + CzechData.GetParameterInfo(param).DisplayName);
                }
                else
                {
                    _lastResult = await _weatherService.FetchPrecipitationAsync(
                        models, days, startHour, endHour, param,
                        _meteologixApiKey, AppendLog, ct, ds, _openMeteoKeyOrUrl, ReportDownloadProgress);
                    if (ct.IsCancellationRequested) return;

                    _resultCache[cacheKey] = _lastResult;
                    _cacheOrder.Remove(cacheKey);
                    _cacheOrder.Add(cacheKey);
                    while (_cacheOrder.Count > 6)
                    {
                        string oldest = _cacheOrder[0];
                        _cacheOrder.RemoveAt(0);
                        _resultCache.Remove(oldest);
                    }
                }

                EnsembleStats.ApplyRollingWindow(_lastResult, GetSelectedRollingHours());
                _lastResult.EffectiveAgreementThreshold = (double)_numWarn.Value;
                _lastResult.WarningThreshold = _chkWarn.Checked ? (double?)_numWarn.Value : null;
                _lastResult.SelectedTimeStepIndex = -1;

                SetupTimelineForResult(_lastResult);
                RefreshAllViewsFromLastResult();
                UpdateDataAgeStatus();

                _btnSaveAll.Enabled = true;
                string offlineTag = _lastResult.IsOfflineFallback ? " [⚠ OFFLINE ZÁLOHA]" : "";
                _lblStatus.Text = "Hotovo [" + (_lastResult.DataSourceLabel ?? "API") + "]" + offlineTag + " (" + _lastResult.PeriodDescription + "). Kliknutím na mapu zobrazíte graf.";
            }
            catch (OperationCanceledException)
            {
                AppendLog("Stahování bylo zrušeno uživatelem.");
                _lblStatus.Text = "Stahování zrušeno.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Chyba při stahování: " + ex.Message);
                AppendLog("CHYBA: " + ex.Message);
                _lblStatus.Text = "Chyba při stahování dat.";
                MessageBox.Show(this, "Nastala chyba při stahování nebo zpracování dat:\n\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnCancelFetch.Visible = false;
                _btnRun.Width = 294;
                _btnRun.Enabled = true;
                _progress.Visible = false;
            }
        }

        private void OnMapMouseMoved(PointF? imgPt)
        {
            if (_cmbMapSelect.SelectedItem == null) return;
            OnAreaMapMouseMoved(_picMap, _cmbMapSelect.SelectedItem.ToString(), imgPt);
        }

        private double? LookupPointValue(GeoGridPoint nearest, string modelKey, out int agreeHits, out int agreeTotal)
        {
            agreeHits = 0;
            agreeTotal = 0;
            if (nearest == null || _lastResult == null) return null;
            int step = _lastResult.SelectedTimeStepIndex;

            if (EnsembleStats.IsSummaryMapKey(modelKey))
            {
                var vals = new List<double>(_lastResult.Models.Count);
                foreach (var m in _lastResult.Models)
                {
                    double? mv = WeatherService.GetGridPointValue(nearest, m.Id, step);
                    if (mv.HasValue)
                        vals.Add(mv.Value);
                }
                return EnsembleStats.ComputeSummaryFromValues(vals, modelKey, _lastResult.EffectiveAgreementThreshold, _lastResult.Parameter, out agreeHits, out agreeTotal);
            }

            return WeatherService.GetGridPointValue(nearest, modelKey, step);
        }

        private void OpenMeteogramAtPixel(PointF imgPt, string modelName)
        {
            if (_lastResult == null || _lastResult.GridPoints == null || _lastResult.GridPoints.Count == 0)
                return;

            string modelKey = (!string.IsNullOrEmpty(modelName) && _modelMapKeys.ContainsKey(modelName))
                ? _modelMapKeys[modelName]
                : null;

            if (_lastResult.Parameter == WeatherParameter.SignificantWeather)
            {
                if (string.IsNullOrEmpty(modelKey) || EnsembleStats.IsSummaryMapKey(modelKey))
                    return;
            }

            double lat, lon;
            if (!MapRenderer.TryImagePixelToLatLon(imgPt, out lat, out lon))
                return;

            string regionCode = CzechData.FindRegionCode(lat, lon);
            if (string.IsNullOrEmpty(regionCode))
                return;

            OpenDialogAtLatLon(lat, lon, regionCode, modelName, modelKey);
        }

        private void OpenDialogAtLatLon(double lat, double lon, string regionCode, string modelName, string modelKey)
        {
            if (_lastResult == null || _lastResult.GridPoints == null || _lastResult.GridPoints.Count == 0)
                return;

            GeoGridPoint nearest = null;
            double bestD2 = double.MaxValue;
            foreach (var gp in _lastResult.GridPoints)
            {
                double d2 = (gp.Latitude - lat) * (gp.Latitude - lat) + (gp.Longitude - lon) * (gp.Longitude - lon);
                if (d2 < bestD2) { bestD2 = d2; nearest = gp; }
            }
            if (nearest == null) return;

            if (_lastResult.Parameter == WeatherParameter.SignificantWeather && !string.IsNullOrEmpty(modelKey) && !EnsembleStats.IsSummaryMapKey(modelKey))
            {
                using (var dlg = new ThermodiagramDialog(_lastResult, nearest, regionCode, modelName, modelKey))
                    dlg.ShowDialog(this);
            }
            else
            {
                using (var dlg = new MeteogramDialog(_lastResult, nearest, regionCode))
                    dlg.ShowDialog(this);
            }
        }

        private void OnOrpRowDoubleClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _lastResult == null) return;
            var orp = _gridOrp.Rows[e.RowIndex].Tag as OrpAreaInfo;
            if (orp == null) return;

            string firstModelName = _lastResult.Models.Count > 0 ? _lastResult.Models[0].DisplayName : null;
            string firstModelId = _lastResult.Models.Count > 0 ? _lastResult.Models[0].Id : null;
            OpenDialogAtLatLon(orp.CenterLat, orp.CenterLon, orp.Name + ", " + orp.RegionCode, firstModelName, firstModelId);
        }

        private void ApplySavedSettings()
        {
            var s = SettingsManager.Load();
            if (s == null) return;

            _suppressAutoFetch = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(s.OutputDir))
                    _outputDir = s.OutputDir;
                if (!string.IsNullOrEmpty(s.MeteologixApiKey))
                    _meteologixApiKey = s.MeteologixApiKey;
                if (!string.IsNullOrEmpty(s.OpenMeteoCustomKeyOrUrl))
                    _openMeteoKeyOrUrl = s.OpenMeteoCustomKeyOrUrl;
                if (s.DataSourceIndex >= 0 && s.DataSourceIndex <= 4)
                    _dataSourceIndex = s.DataSourceIndex;
                if (!string.IsNullOrWhiteSpace(s.UpdateManifestUrl))
                    _updateManifestUrl = s.UpdateManifestUrl;
                if (s.ParamIndex >= 0 && s.ParamIndex < _cmbParam.Items.Count)
                    _cmbParam.SelectedIndex = s.ParamIndex;
                if (s.AggModeIndex >= 0 && s.AggModeIndex < _cmbAggMode.Items.Count)
                    _cmbAggMode.SelectedIndex = s.AggModeIndex;
                if (s.ForecastDaysIndex >= 0 && s.ForecastDaysIndex < _cmbDays.Items.Count)
                    _cmbDays.SelectedIndex = s.ForecastDaysIndex;
                if (s.AutoHoursIndex >= 0 && s.AutoHoursIndex < _cmbAutoHoursCount())
                    _autoHoursIndex = s.AutoHoursIndex;
                _autoRunEnabled = s.AutoRunEnabled;

                if (s.UtciWarningThreshold >= -50 && s.UtciWarningThreshold <= 5000)
                    _numWarn.Value = (decimal)s.UtciWarningThreshold;

                // NOTE: Model checkboxes intentionally stay UNCHECKED on startup to avoid unwanted API calls!
                UpdateSettingsSummaryLabel();
            }
            finally
            {
                _suppressAutoFetch = false;
            }
        }

        private int _cmbAutoHoursCount()
        {
            return 4;
        }

        private void SaveCurrentSettings()
        {
            var s = new AppSettings
            {
                OutputDir = (_outputDir ?? "").Trim(),
                MeteologixApiKey = _meteologixApiKey,
                OpenMeteoCustomKeyOrUrl = _openMeteoKeyOrUrl,
                DataSourceIndex = _dataSourceIndex,
                UpdateManifestUrl = _updateManifestUrl,
                ParamIndex = _cmbParam.SelectedIndex,
                AggModeIndex = _cmbAggMode.SelectedIndex,
                ForecastDaysIndex = _cmbDays.SelectedIndex,
                AutoHoursIndex = _autoHoursIndex,
                AutoRunEnabled = _autoRunEnabled,
                UtciWarningThreshold = (double)_numWarn.Value
            };
            SettingsManager.Save(s);
        }

        private void OpenUpdateDialog()
        {
            using (var dlg = new UpdateDialog(_updateManifestUrl))
            {
                dlg.ShowDialog(this);
                _updateManifestUrl = dlg.ManifestUrl;
                SaveCurrentSettings();
            }
        }

        private async System.Threading.Tasks.Task CheckForUpdatesOnStartupAsync()
        {
            if (string.IsNullOrWhiteSpace(_updateManifestUrl))
                return;
            try
            {
                var info = await UpdateService.CheckForUpdateAsync(_updateManifestUrl);
                if (info.IsNewer)
                {
                    var res = MessageBox.Show(
                        this,
                        "Je dostupná nová verze aplikace MeteoBarbucha ČR (" + info.Version + ").\n\n" +
                        (string.IsNullOrEmpty(info.Notes) ? "" : info.Notes + "\n\n") +
                        "Chcete ji nyní stáhnout a aktualizovat?",
                        "Dostupná aktualizace",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (res == DialogResult.Yes)
                    {
                        _lblStatus.Text = "Stahuji aktualizaci " + info.Version + "...";
                        await UpdateService.DownloadAndApplyUpdateAsync(info.DownloadUrl);
                    }
                }
            }
            catch
            {
            }
        }

        private void BuildSummaryMaps(ForecastResult result)
        {
            var regionalTable = WeatherService.ComputeRegionalTable(result, GetSelectedAggMode());
            string prevSelected = _cmbMapSelect.SelectedItem != null ? _cmbMapSelect.SelectedItem.ToString() : null;

            _modelMapsData.Clear();
            _modelMapKeys.Clear();
            _cmbMapSelect.Items.Clear();

            string op = EnsembleStats.IsBelowThresholdWarning(result.Parameter) ? "≤" : "≥";
            string thStr = result.EffectiveAgreementThreshold.ToString("0.#", CultureInfo.InvariantCulture) + " " + result.Unit;

            var summaryDefs = new[]
            {
                Tuple.Create("Shoda modelů " + op + " " + thStr + " (%)", EnsembleStats.KeyAgreement),
                Tuple.Create("Medián modelů (P50)", EnsembleStats.KeyMedian),
                Tuple.Create("Percentil P90 (horní scénář)", EnsembleStats.KeyP90),
                Tuple.Create("Percentil P10 (spodní odhad)", EnsembleStats.KeyP10),
                Tuple.Create("Průměr všech modelů", EnsembleStats.KeyMean),
                Tuple.Create("Extrém / Maximum ze všech modelů", EnsembleStats.KeyMax),
                Tuple.Create("Rozptyl modelů (Max - Min)", EnsembleStats.KeySpread)
            };

            var rowVals = new List<double>();
            foreach (var def in summaryDefs)
            {
                var mapDict = new Dictionary<string, double?>();
                foreach (var r in CzechData.Regions)
                {
                    rowVals.Clear();
                    foreach (var m in result.Models)
                    {
                        double? v;
                        regionalTable[r.Code].TryGetValue(m.Id, out v);
                        if (v.HasValue) rowVals.Add(v.Value);
                    }
                    int hits, tot;
                    mapDict[r.Code] = EnsembleStats.ComputeSummaryFromValues(rowVals, def.Item2, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot);
                }

                _modelMapsData[def.Item1] = mapDict;
                _modelMapKeys[def.Item1] = def.Item2;
                _cmbMapSelect.Items.Add(def.Item1);
            }

            foreach (var model in result.Models)
            {
                var modelValues = new Dictionary<string, double?>();
                foreach (var r in CzechData.Regions)
                {
                    double? v;
                    regionalTable[r.Code].TryGetValue(model.Id, out v);
                    modelValues[r.Code] = v;
                }
                string title = model.DisplayName;
                if (EnsembleStats.IsModelRunStale(result, model.Id))
                    title = "⚠ " + title;

                _modelMapsData[title] = modelValues;
                _modelMapKeys[title] = model.Id;
                _cmbMapSelect.Items.Add(title);
            }

            int restoreIdx = prevSelected != null ? _cmbMapSelect.Items.IndexOf(prevSelected) : -1;
            _cmbMapSelect.SelectedIndex = restoreIdx >= 0 ? restoreIdx : 1; // Default to Medián (P50)

            PopulateMultiView();
        }

        private async void PopulateMultiView()
        {
            _multiViewPanel.SuspendLayout();
            _areaMultiViewPanel.SuspendLayout();
            _multiViewPanel.Controls.Clear();
            _areaMultiViewPanel.Controls.Clear();
            _areaCards.Clear();
            foreach (var bmp in _multiViewThumbnails)
                bmp.Dispose();
            _multiViewThumbnails.Clear();

            if (_lastResult == null)
            {
                _multiViewPanel.ResumeLayout();
                _areaMultiViewPanel.ResumeLayout();
                return;
            }

            var renderJobs = new List<Tuple<string, string, Dictionary<string, double?>>>();
            foreach (var item in _cmbMapSelect.Items)
            {
                string modelName = item.ToString();
                Dictionary<string, double?> values;
                if (!_modelMapsData.TryGetValue(modelName, out values))
                    continue;
                string modelKey = _modelMapKeys.ContainsKey(modelName) ? _modelMapKeys[modelName] : null;
                renderJobs.Add(Tuple.Create(modelName, modelKey, values));
            }
            var resultRef = _lastResult;

            var rendered = await Task.Run(() =>
            {
                var list = new List<Tuple<string, string, Bitmap>>();
                foreach (var job in renderJobs)
                {
                    Bitmap bmp = MapRenderer.RenderMapBitmap(job.Item2, job.Item1, resultRef, job.Item3);
                    list.Add(Tuple.Create(job.Item1, job.Item2, bmp));
                }
                return list;
            });

            if (_lastResult != resultRef)
            {
                foreach (var r in rendered) r.Item3.Dispose();
                _multiViewPanel.ResumeLayout();
                _areaMultiViewPanel.ResumeLayout();
                return;
            }

            foreach (var entry in rendered)
            {
                string modelName = entry.Item1;
                string modelKey = entry.Item2;
                Bitmap fullBmp = entry.Item3;
                _multiViewThumbnails.Add(fullBmp);

                bool stale = EnsembleStats.IsModelRunStale(resultRef, modelKey);
                Color headerCol = stale ? Color.FromArgb(180, 83, 9) : Color.FromArgb(15, 23, 42);

                var card = new Panel
                {
                    Size = new Size(400, 278),
                    Margin = new Padding(8),
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Cursor = Cursors.Hand
                };

                var cardTitle = new Label
                {
                    Text = modelName,
                    Dock = DockStyle.Top,
                    Height = 28,
                    BackColor = headerCol,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(8, 0, 0, 0),
                    Cursor = Cursors.Hand
                };

                var pic = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = fullBmp,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(245, 248, 252),
                    Cursor = Cursors.Hand
                };

                EventHandler openDetail = (s, e) => SwitchToDetailView(modelName);
                card.Click += openDetail;
                cardTitle.Click += openDetail;
                pic.Click += openDetail;

                card.Controls.Add(pic);
                card.Controls.Add(cardTitle);
                _multiViewPanel.Controls.Add(card);

                var areaCard = new Panel
                {
                    Size = new Size(400, 278),
                    Margin = new Padding(8),
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var areaTitle = new Label
                {
                    Text = modelName,
                    Dock = DockStyle.Top,
                    Height = 28,
                    BackColor = headerCol,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(8, 0, 0, 0)
                };

                var areaPic = new ZoomablePictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = fullBmp,
                    BackColor = Color.FromArgb(245, 248, 252)
                };
                areaPic.SetView(_areaZoom, _areaPan, false);
                areaPic.ViewChanged += (z, p) => SyncAreaView(areaPic, z, p);
                areaPic.ImageMouseMoved += pt => OnAreaMapMouseMoved(areaPic, modelName, pt);
                areaPic.ImageClicked += pt => OpenMeteogramAtPixel(pt, modelName);

                _areaCards.Add(areaPic);
                areaCard.Controls.Add(areaPic);
                areaCard.Controls.Add(areaTitle);
                _areaMultiViewPanel.Controls.Add(areaCard);
            }

            _multiViewPanel.ResumeLayout();
            _areaMultiViewPanel.ResumeLayout();
            if (!_picMap.Visible)
                SwitchToMultiView();
        }

        private void SyncAreaView(ZoomablePictureBox source, float zoom, PointF pan)
        {
            if (_syncingAreaView) return;
            _syncingAreaView = true;
            try
            {
                _areaZoom = zoom;
                _areaPan = pan;
                foreach (var card in _areaCards)
                {
                    if (card != source)
                        card.SetView(zoom, pan, false);
                }
            }
            finally
            {
                _syncingAreaView = false;
            }
        }

        private void ZoomAllAreaCards(float factor)
        {
            float newZoom = Math.Max(0.8f, Math.Min(8.0f, _areaZoom * factor));
            _areaZoom = newZoom;
            SyncAreaView(null, _areaZoom, _areaPan);
        }

        private void ApplyAreaPreset(int index)
        {
            float zoom = 1.0f;
            PointF pan = PointF.Empty;
            switch (index)
            {
                case 1: zoom = 2.6f; pan = new PointF(0.11f, 0.04f); break;
                case 2: zoom = 2.5f; pan = new PointF(0.11f, -0.21f); break;
                case 3: zoom = 2.6f; pan = new PointF(0.28f, 0.00f); break;
                case 4: zoom = 2.6f; pan = new PointF(0.20f, 0.19f); break;
                case 5: zoom = 2.8f; pan = new PointF(0.01f, 0.22f); break;
                case 6: zoom = 2.5f; pan = new PointF(-0.07f, 0.08f); break;
                case 7: zoom = 2.6f; pan = new PointF(-0.03f, -0.09f); break;
                case 8: zoom = 2.5f; pan = new PointF(-0.14f, -0.17f); break;
                case 9: zoom = 2.6f; pan = new PointF(-0.22f, 0.06f); break;
                case 10: zoom = 2.7f; pan = new PointF(-0.33f, 0.02f); break;
            }
            _areaZoom = zoom;
            _areaPan = pan;
            SyncAreaView(null, zoom, pan);
        }

        private void OnAreaMapMouseMoved(ZoomablePictureBox targetBox, string modelName, PointF? imgPt)
        {
            if (!imgPt.HasValue || _lastResult == null || _lastResult.GridPoints == null || _lastResult.GridPoints.Count == 0)
            {
                if (targetBox != null) targetBox.SetHoverOverlay(null, Point.Empty);
                return;
            }

            double lat, lon;
            if (!MapRenderer.TryImagePixelToLatLon(imgPt.Value, out lat, out lon))
            {
                if (targetBox != null) targetBox.SetHoverOverlay(null, Point.Empty);
                return;
            }

            string regionCode = CzechData.FindRegionCode(lat, lon);
            if (string.IsNullOrEmpty(regionCode))
            {
                if (targetBox != null) targetBox.SetHoverOverlay(null, Point.Empty);
                return;
            }

            GeoGridPoint nearest = null;
            double bestD2 = double.MaxValue;
            foreach (var gp in _lastResult.GridPoints)
            {
                double d2 = (gp.Latitude - lat) * (gp.Latitude - lat) + (gp.Longitude - lon) * (gp.Longitude - lon);
                if (d2 < bestD2) { bestD2 = d2; nearest = gp; }
            }
            if (nearest == null) return;

            string modelKey = _modelMapKeys.ContainsKey(modelName) ? _modelMapKeys[modelName] : null;
            int hits, tot;
            double? val = LookupPointValue(nearest, modelKey, out hits, out tot);

            string valText;
            if (!val.HasValue)
            {
                valText = "N/A";
            }
            else if (modelKey == EnsembleStats.KeyAgreement)
            {
                valText = string.Format(CultureInfo.InvariantCulture, "{0:0} % ({1}/{2} modelů)", val.Value, hits, tot);
            }
            else if (_lastResult.Parameter == WeatherParameter.SignificantWeather)
            {
                valText = CzechData.GetWmoShortLabel(val.Value) + " (" + ((int)Math.Round(val.Value)) + ")";
            }
            else
            {
                valText = val.Value.ToString("0.0", CultureInfo.InvariantCulture) + " " + _lastResult.Unit;
            }

            bool isWarn = modelKey != EnsembleStats.KeyAgreement &&
                          _lastResult.WarningThreshold.HasValue &&
                          val.HasValue &&
                          EnsembleStats.ExceedsThreshold(val.Value, _lastResult.WarningThreshold.Value, _lastResult.Parameter);
            string overlayText = (isWarn ? "⚠ " : "") + valText + " (" + regionCode + ")" + (isWarn ? " — VÝSTRAHA!" : "");
            if (targetBox != null)
                targetBox.SetHoverOverlay(overlayText, targetBox.PointToClient(Cursor.Position));
            _lblStatus.Text = string.Format(CultureInfo.InvariantCulture, "Kurzor: {0:0.00}° N, {1:0.00}° E ({2})  |  {3}: {4}{5}", lat, lon, regionCode, modelName, valText, isWarn ? "  [⚠ VÝSTRAHA]" : "");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Keyboard shortcuts for timeline when focus is not inside a text/number editor
            if (!(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown) && !(ActiveControl is ComboBox))
            {
                if (keyData == Keys.Space && _trkTimeStep.Enabled)
                {
                    ToggleTimelinePlayback();
                    return true;
                }

                bool onAreaZoomed = (_tabs != null && _tabs.SelectedTab == _tabArea && _areaZoom > 1.05f);
                if ((keyData == (Keys.Alt | Keys.Left) || (!onAreaZoomed && keyData == Keys.Left)) && _trkTimeStep.Enabled)
                {
                    StopTimelinePlayback();
                    _trkTimeStep.Value = Math.Max(_trkTimeStep.Minimum, _trkTimeStep.Value - 1);
                    return true;
                }
                if ((keyData == (Keys.Alt | Keys.Right) || (!onAreaZoomed && keyData == Keys.Right)) && _trkTimeStep.Enabled)
                {
                    StopTimelinePlayback();
                    _trkTimeStep.Value = Math.Min(_trkTimeStep.Maximum, _trkTimeStep.Value + 1);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_tabs != null && _tabs.SelectedTab == _tabArea && !(ActiveControl is TextBoxBase))
            {
                if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
                {
                    ZoomAllAreaCards(1.25f);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
                {
                    ZoomAllAreaCards(1f / 1.25f);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)
                {
                    _cmbAreaPreset.SelectedIndex = 0;
                    ApplyAreaPreset(0);
                    e.Handled = true;
                }
                else if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && !e.Alt)
                {
                    float step = 0.04f;
                    if (e.KeyCode == Keys.Left) _areaPan.X += step;
                    if (e.KeyCode == Keys.Right) _areaPan.X -= step;
                    if (e.KeyCode == Keys.Up) _areaPan.Y += step;
                    if (e.KeyCode == Keys.Down) _areaPan.Y -= step;
                    SyncAreaView(null, _areaZoom, _areaPan);
                    e.Handled = true;
                }
            }
        }

        private void SwitchToDetailView(string modelName)
        {
            _multiViewPanel.Visible = false;
            _picMap.Visible = true;
            _btnBackToMultiView.Visible = true;
            _btnResetZoom.Visible = true;
            _lblSelectMap.Enabled = true;
            _cmbMapSelect.Enabled = true;
            _lblZoomHint.Text = "Kolečko: zoom | Tažení: posun | Klik: graf / sondáž | Dvojklik: reset";

            int idx = _cmbMapSelect.Items.IndexOf(modelName);
            if (idx >= 0)
                _cmbMapSelect.SelectedIndex = idx;
            DisplaySelectedMap();
        }

        private void SwitchToMultiView()
        {
            _picMap.Visible = false;
            _multiViewPanel.Visible = true;
            _btnBackToMultiView.Visible = false;
            _btnResetZoom.Visible = false;
            _lblSelectMap.Enabled = false;
            _cmbMapSelect.Enabled = false;
            _lblZoomHint.Text = "Klikněte na libovolnou mapu pro detailní zoom a posun";
        }

        private void ExportOrpCsvOnly()
        {
            if (_lastResult == null) return;
            try
            {
                string stamp = _lastResult.FetchedAt.ToString("yyyy-MM-dd_HH-mm-ss");
                string prefix = CzechData.GetParameterInfo(_lastResult.Parameter).FilePrefix;
                string csvPath = Path.Combine(_outputDir, "tabulky", prefix + "_ORP_" + stamp + ".csv");
                CsvExporter.ExportOrpToCsv(csvPath, _lastResult, GetSelectedAggMode());
                AppendLog("Uložena CSV tabulka ORP: " + csvPath);
                MessageBox.Show(this, "Tabulka všech 206 ORP byla uložena do:\n" + csvPath, "Export ORP dokončen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Chyba při exportu ORP CSV:\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveAllResults(bool showDialog)
        {
            if (_lastResult == null || _modelMapsData.Count == 0)
                return;

            string baseDir = (_outputDir ?? "").Trim();
            if (string.IsNullOrEmpty(baseDir))
            {
                MessageBox.Show(this, "V Nastavení zadejte platnou výstupní složku.", "Upozornění", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string stamp = _lastResult.FetchedAt.ToString("yyyy-MM-dd_HH-mm-ss");
                string prefix = CzechData.GetParameterInfo(_lastResult.Parameter).FilePrefix;
                string snapshotsDir = Path.Combine(baseDir, "snimky", prefix + "_" + stamp);
                string csvKrajePath = Path.Combine(baseDir, "tabulky", prefix + "_kraje_" + stamp + ".csv");
                string csvOrpPath = Path.Combine(baseDir, "tabulky", prefix + "_ORP_" + stamp + ".csv");

                int savedCount = 0;
                foreach (var kvp in _modelMapsData)
                {
                    string fileKey = _modelMapKeys[kvp.Key];
                    string pngPath = MapRenderer.SaveModelMapSnapshot(snapshotsDir, fileKey, kvp.Key, _lastResult, kvp.Value);
                    savedCount++;
                }

                CsvExporter.ExportToCsv(csvKrajePath, _lastResult, GetSelectedAggMode());
                CsvExporter.ExportOrpToCsv(csvOrpPath, _lastResult, GetSelectedAggMode());
                AppendLog("Uloženo " + savedCount + " PNG map + CSV tabulky krajů a ORP do: " + baseDir);

                _lblStatus.Text = "Uloženo " + savedCount + " snímků + tabulky Krajů a ORP (" + stamp + ").";
                if (showDialog)
                    MessageBox.Show(this, "Uloženo " + savedCount + " mapových snímků a CSV tabulky (14 krajů + 206 ORP) do složky:\n" + baseDir, "Uložení dokončeno", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Chyba při ukládání: " + ex.Message);
                AppendLog("CHYBA při ukládání: " + ex.Message);
                if (showDialog)
                    MessageBox.Show(this, "Chyba při ukládání souborů:\n\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void StyleValueCell(DataGridViewCell cell, double val, ForecastResult result, bool isAgreementCol)
        {
            Color bg = isAgreementCol
                ? MapRenderer.GetAgreementColor(val)
                : MapRenderer.GetValueColor(val, result.Parameter);
            Color fg = MapRenderer.GetIdealTextColor(bg);
            cell.Style.BackColor = bg;
            cell.Style.ForeColor = fg;

            if (!isAgreementCol && result.WarningThreshold.HasValue && EnsembleStats.ExceedsThreshold(val, result.WarningThreshold.Value, result.Parameter))
            {
                cell.Style.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
                cell.Value = "⚠ " + Convert.ToString(cell.Value, CultureInfo.InvariantCulture);
            }
        }

        private void PopulateGrid(ForecastResult result)
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();

            string unit = string.IsNullOrEmpty(result.Unit) ? "mm" : result.Unit;
            var regionalTable = WeatherService.ComputeRegionalTable(result, GetSelectedAggMode());

            _grid.Columns.Add("Code", "Kód");
            _grid.Columns["Code"].FillWeight = 36;

            _grid.Columns.Add("Name", "Kraj");
            _grid.Columns["Name"].FillWeight = 115;

            foreach (var m in result.Models)
            {
                bool stale = EnsembleStats.IsModelRunStale(result, m.Id);
                bool derived = result.ModelNotes != null && result.ModelNotes.ContainsKey(m.Id);
                string hdr = m.DisplayName + (derived ? " *" : "") + (stale ? "\n⚠ >12h (" + unit + ")" : "\n(" + unit + ")");

                var col = new DataGridViewTextBoxColumn
                {
                    Name = m.Id,
                    HeaderText = hdr,
                    FillWeight = 68,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                };
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                if (stale)
                {
                    col.HeaderCell.Style.BackColor = Color.FromArgb(254, 243, 199);
                    col.HeaderCell.Style.ForeColor = Color.FromArgb(180, 83, 9);
                }
                _grid.Columns.Add(col);
            }

            AddSummaryColumn(_grid, "Med", "Medián\n(" + unit + ")", true);
            AddSummaryColumn(_grid, "P10", "P10\n(" + unit + ")", false);
            AddSummaryColumn(_grid, "P90", "P90\n(" + unit + ")", false);
            AddSummaryColumn(_grid, "Avg", "Průměr\n(" + unit + ")", false);
            string op = EnsembleStats.IsBelowThresholdWarning(result.Parameter) ? "≤" : "≥";
            AddSummaryColumn(_grid, "Agree", "Shoda " + op + " " + result.EffectiveAgreementThreshold.ToString("0.#", CultureInfo.InvariantCulture) + "\n(%)", true);

            var modelTotals = new Dictionary<string, double>();
            var modelCounts = new Dictionary<string, int>();
            foreach (var m in result.Models)
            {
                modelTotals[m.Id] = 0;
                modelCounts[m.Id] = 0;
            }

            var rowVals = new List<double>();
            foreach (var r in CzechData.Regions)
            {
                int rowIdx = _grid.Rows.Add();
                var row = _grid.Rows[rowIdx];
                row.Cells["Code"].Value = r.Code;
                row.Cells["Name"].Value = r.Name;
                rowVals.Clear();

                foreach (var m in result.Models)
                {
                    double? val;
                    regionalTable[r.Code].TryGetValue(m.Id, out val);
                    if (val.HasValue)
                    {
                        row.Cells[m.Id].Value = result.Parameter == WeatherParameter.SignificantWeather
                            ? ((int)Math.Round(val.Value)) + " — " + CzechData.GetWmoShortLabel(val.Value)
                            : val.Value.ToString("0.0", CultureInfo.InvariantCulture);
                        StyleValueCell(row.Cells[m.Id], val.Value, result, false);
                        rowVals.Add(val.Value);
                        modelTotals[m.Id] += val.Value;
                        modelCounts[m.Id]++;
                    }
                    else
                    {
                        row.Cells[m.Id].Value = "—";
                        row.Cells[m.Id].Style.ForeColor = Color.Gray;
                    }
                }

                if (rowVals.Count > 0)
                {
                    int hits, tot;
                    double med = EnsembleStats.ComputePercentile(rowVals, 50.0).Value;
                    double p10 = EnsembleStats.ComputePercentile(rowVals, 10.0).Value;
                    double p90 = EnsembleStats.ComputePercentile(rowVals, 90.0).Value;
                    double avg = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyMean, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot).Value;
                    double ag = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyAgreement, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot).Value;

                    SetSummaryCell(row.Cells["Med"], med, result, false);
                    SetSummaryCell(row.Cells["P10"], p10, result, false);
                    SetSummaryCell(row.Cells["P90"], p90, result, false);
                    SetSummaryCell(row.Cells["Avg"], avg, result, false);

                    row.Cells["Agree"].Value = string.Format(CultureInfo.InvariantCulture, "{0:0}% ({1}/{2})", ag, hits, tot);
                    StyleValueCell(row.Cells["Agree"], ag, result, true);
                }
            }

            // Summary row: Průměr ČR
            int sumIdx = _grid.Rows.Add();
            var sumRow = _grid.Rows[sumIdx];
            sumRow.DefaultCellStyle.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            sumRow.DefaultCellStyle.BackColor = Color.FromArgb(226, 232, 240);
            sumRow.Cells["Code"].Value = "ČR";
            sumRow.Cells["Name"].Value = "Průměr za ČR";

            var crVals = new List<double>();
            foreach (var m in result.Models)
            {
                if (modelCounts[m.Id] > 0)
                {
                    double avg = modelTotals[m.Id] / modelCounts[m.Id];
                    sumRow.Cells[m.Id].Value = avg.ToString("0.0", CultureInfo.InvariantCulture);
                    crVals.Add(avg);
                }
            }
            if (crVals.Count > 0)
            {
                int ch, ct2;
                sumRow.Cells["Med"].Value = EnsembleStats.ComputePercentile(crVals, 50.0).Value.ToString("0.0", CultureInfo.InvariantCulture);
                sumRow.Cells["P10"].Value = EnsembleStats.ComputePercentile(crVals, 10.0).Value.ToString("0.0", CultureInfo.InvariantCulture);
                sumRow.Cells["P90"].Value = EnsembleStats.ComputePercentile(crVals, 90.0).Value.ToString("0.0", CultureInfo.InvariantCulture);
                sumRow.Cells["Avg"].Value = EnsembleStats.ComputeSummaryFromValues(crVals, EnsembleStats.KeyMean, result.EffectiveAgreementThreshold, result.Parameter, out ch, out ct2).Value.ToString("0.0", CultureInfo.InvariantCulture);
            }
        }

        private static void AddSummaryColumn(DataGridView grid, string name, string header, bool bold)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = 62
            };
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (bold)
                col.DefaultCellStyle.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            grid.Columns.Add(col);
        }

        private static void SetSummaryCell(DataGridViewCell cell, double val, ForecastResult result, bool isAgree)
        {
            cell.Value = val.ToString("0.0", CultureInfo.InvariantCulture);
            StyleValueCell(cell, val, result, isAgree);
        }

        private void PopulateOrpGrid(ForecastResult result)
        {
            _gridOrp.SuspendLayout();
            _gridOrp.Columns.Clear();
            _gridOrp.Rows.Clear();

            string unit = string.IsNullOrEmpty(result.Unit) ? "mm" : result.Unit;
            var orps = OrpCatalog.GetAllOrps();
            var orpTable = OrpCatalog.ComputeOrpTable(result, GetSelectedAggMode());
            string filter = (_txtOrpFilter.Text ?? "").Trim();
            bool warnOnly = _chkOrpWarnOnly.Checked;

            _gridOrp.Columns.Add("Kraj", "Kraj");
            _gridOrp.Columns.Add("ORP", "Obec s rozšířenou působností (ORP)");

            string op = EnsembleStats.IsBelowThresholdWarning(result.Parameter) ? "≤" : "≥";
            AddOrpNumericCol("Med", "Medián (" + unit + ")", true);
            AddOrpNumericCol("P90", "P90 (" + unit + ")", false);
            AddOrpNumericCol("P10", "P10 (" + unit + ")", false);
            AddOrpNumericCol("Max", "Extrém (" + unit + ")", false);
            AddOrpNumericCol("Agree", "Shoda " + op + " " + result.EffectiveAgreementThreshold.ToString("0.#", CultureInfo.InvariantCulture) + " (%)", true);

            foreach (var m in result.Models)
            {
                AddOrpNumericCol(m.Id, m.DisplayName + " (" + unit + ")", false);
            }

            var rowVals = new List<double>();
            foreach (var orp in orps)
            {
                if (!string.IsNullOrEmpty(filter))
                {
                    if (orp.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        orp.RegionCode.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                        continue;
                }

                rowVals.Clear();
                var rowMap = orpTable[orp.Name];
                foreach (var m in result.Models)
                {
                    double? v;
                    if (rowMap.TryGetValue(m.Id, out v) && v.HasValue)
                        rowVals.Add(v.Value);
                }

                int hits = 0, tot = 0;
                double? ag = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyAgreement, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot);
                if (warnOnly && (!ag.HasValue || ag.Value < 50.0))
                    continue;

                int rIdx = _gridOrp.Rows.Add();
                var row = _gridOrp.Rows[rIdx];
                row.Tag = orp;
                row.Cells["Kraj"].Value = orp.RegionCode;
                row.Cells["ORP"].Value = orp.Name;

                if (rowVals.Count > 0)
                {
                    double med = EnsembleStats.ComputePercentile(rowVals, 50.0).Value;
                    double p90 = EnsembleStats.ComputePercentile(rowVals, 90.0).Value;
                    double p10 = EnsembleStats.ComputePercentile(rowVals, 10.0).Value;
                    double mx = EnsembleStats.ComputeSummaryFromValues(rowVals, EnsembleStats.KeyMax, result.EffectiveAgreementThreshold, result.Parameter, out hits, out tot).Value;

                    SetNumericSortableCell(row.Cells["Med"], med, result, false);
                    SetNumericSortableCell(row.Cells["P90"], p90, result, false);
                    SetNumericSortableCell(row.Cells["P10"], p10, result, false);
                    SetNumericSortableCell(row.Cells["Max"], mx, result, false);
                    row.Cells["Agree"].Value = Math.Round(ag.Value, 0);
                    StyleValueCell(row.Cells["Agree"], ag.Value, result, true);
                }

                foreach (var m in result.Models)
                {
                    double? val;
                    rowMap.TryGetValue(m.Id, out val);
                    if (val.HasValue)
                    {
                        SetNumericSortableCell(row.Cells[m.Id], val.Value, result, false);
                    }
                    else
                    {
                        row.Cells[m.Id].Value = null;
                    }
                }
            }

            _gridOrp.ResumeLayout();
        }

        private void AddOrpNumericCol(string name, string header, bool bold)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                ValueType = typeof(double)
            };
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            col.DefaultCellStyle.Format = name == "Agree" ? "0'%'" : "0.0";
            col.DefaultCellStyle.NullValue = "—";
            if (bold)
                col.DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _gridOrp.Columns.Add(col);
        }

        private static void SetNumericSortableCell(DataGridViewCell cell, double val, ForecastResult result, bool isAgree)
        {
            cell.Value = Math.Round(val, 1);
            Color bg = isAgree ? MapRenderer.GetAgreementColor(val) : MapRenderer.GetValueColor(val, result.Parameter);
            cell.Style.BackColor = bg;
            cell.Style.ForeColor = MapRenderer.GetIdealTextColor(bg);
            if (!isAgree && result.WarningThreshold.HasValue && EnsembleStats.ExceedsThreshold(val, result.WarningThreshold.Value, result.Parameter))
            {
                cell.Style.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            }
        }

        private void DisplaySelectedMap()
        {
            if (_cmbMapSelect.SelectedItem == null || _lastResult == null) return;
            string key = _cmbMapSelect.SelectedItem.ToString();
            Dictionary<string, double?> values;
            if (_modelMapsData.TryGetValue(key, out values))
            {
                if (_currentPreviewBitmap != null)
                {
                    _picMap.Image = null;
                    _currentPreviewBitmap.Dispose();
                    _currentPreviewBitmap = null;
                }
                string modelKey = _modelMapKeys.ContainsKey(key) ? _modelMapKeys[key] : null;
                _currentPreviewBitmap = MapRenderer.RenderMapBitmap(modelKey, key, _lastResult, values);
                _picMap.Image = _currentPreviewBitmap;
            }
        }
    }
}
