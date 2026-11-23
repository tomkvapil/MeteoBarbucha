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
        private ForecastResult _lastResult;
        private readonly Dictionary<string, Dictionary<string, double?>> _modelMapsData = new Dictionary<string, Dictionary<string, double?>>();
        private readonly Dictionary<string, string> _modelMapKeys = new Dictionary<string, string>();
        private readonly Dictionary<string, ForecastResult> _resultCache = new Dictionary<string, ForecastResult>();
        private readonly List<string> _cacheOrder = new List<string>();
        private CancellationTokenSource _fetchCts;
        private bool _suppressAutoFetch;
        private Bitmap _currentPreviewBitmap;

        private ComboBox _cmbParam;
        private CheckBox _chkWarn;
        private NumericUpDown _numWarn;
        private TrackBar _trkTimeStep;
        private Label _lblTimeStep;
        private ComboBox _cmbAggMode;
        private RadioButton _rbDays;
        private RadioButton _rbHours;
        private ComboBox _cmbDays;
        private DateTimePicker _dtpStart;
        private DateTimePicker _dtpEnd;
        private Label _lblHourRange;
        private CheckedListBox _chkModels;
        private TextBox _txtApiKey;
        private TextBox _txtOutputDir;
        private Button _btnBrowse;
        private Button _btnRun;
        private Button _btnSaveAll;
        private CheckBox _chkAutoRun;
        private ComboBox _cmbAutoHours;
        private System.Windows.Forms.Timer _autoTimer;
        private Button _btnOpenFolder;
        private Button _btnOpenWeb;
        private Button _btnFeedback;
        private Button _btnHelp;
        private Button _btnUpdate;
        private string _updateManifestUrl = UpdateService.DefaultManifestUrl;
        private DataGridView _grid;
        private ComboBox _cmbMapSelect;
        private Label _lblSelectMap;
        private ZoomablePictureBox _picMap;
        private FlowLayoutPanel _multiViewPanel;
        private Button _btnBackToMultiView;
        private Button _btnResetZoom;
        private Label _lblZoomHint;
        private TabControl _tabs;
        private TabPage _tabArea;
        private FlowLayoutPanel _areaMultiViewPanel;
        private ComboBox _cmbAreaPreset;
        private readonly List<ZoomablePictureBox> _areaCards = new List<ZoomablePictureBox>();
        private float _areaZoom = 1.0f;
        private PointF _areaPan = PointF.Empty;
        private bool _syncingAreaView;
        private readonly List<Bitmap> _multiViewThumbnails = new List<Bitmap>();
        private TextBox _txtLog;
        private ToolStripStatusLabel _lblStatus;
        private ToolStripProgressBar _progress;

        public MainForm()
        {
            InitializeUi();
            ApplySavedSettings();
            FormClosing += (s, e) => SaveCurrentSettings();
            Shown += async (s, e) => await CheckForUpdatesOnStartupAsync();
        }

        private void InitializeUi()
        {
            Text = "MeteoBarbucha ČR — Multi-Model Analyzátor";
            Size = new Size(1380, 820);
            MinimumSize = new Size(1340, 720);
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
                Text = "Stahování a výpočet sum srážek pro všech 14 krajů ČR napříč meteorologickými modely",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(20, 40)
            };

            _btnOpenFolder = new Button
            {
                Text = "Složka výstupů",
                Size = new Size(125, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(header.Width - 760, 17),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnOpenFolder.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            _btnOpenFolder.Click += (s, e) => OpenOutputFolder();

            _btnOpenWeb = new Button
            {
                Text = "Web Meteologix",
                Size = new Size(130, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(header.Width - 627, 17),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnOpenWeb.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            _btnOpenWeb.Click += (s, e) =>
            {
                try { Process.Start("https://meteologix.com/cz/model-charts/euro/czech-republic/accumulated-precipitation"); }
                catch { }
            };

            _btnFeedback = new Button
            {
                Text = "Máte návrh na změnu aplikace?",
                Size = new Size(225, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(header.Width - 489, 17),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnFeedback.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
            _btnFeedback.Click += (s, e) =>
            {
                try
                {
                    Process.Start("mailto:tom.kvapil89@gmail.com?subject=" + Uri.EscapeDataString("Návrh na změnu aplikace MeteoBarbucha ČR"));
                }
                catch { }
            };

            _btnHelp = new Button
            {
                Text = "Návod k použití",
                Size = new Size(125, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(header.Width - 256, 17),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnHelp.FlatAppearance.BorderColor = Color.FromArgb(5, 150, 105);
            _btnHelp.Click += (s, e) =>
            {
                using (var dlg = new HelpDialog())
                    dlg.ShowDialog(this);
            };

            _btnUpdate = new Button
            {
                Text = "Aktualizace",
                Size = new Size(110, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(header.Width - 123, 17),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnUpdate.FlatAppearance.BorderColor = Color.FromArgb(100, 116, 139);
            _btnUpdate.Click += (s, e) => OpenUpdateDialog();

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblCopyright);
            header.Controls.Add(lblSub);
            header.Controls.Add(_btnOpenFolder);
            header.Controls.Add(_btnOpenWeb);
            header.Controls.Add(_btnFeedback);
            header.Controls.Add(_btnHelp);
            header.Controls.Add(_btnUpdate);

            // Left Sidebar
            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 325,
                BackColor = Color.White,
                Padding = new Padding(16),
                AutoScroll = true
            };

            int y = 12;

            var lblParam = new Label { Text = "Parametr předpovědi:", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(16, y), AutoSize = true };
            y += 22;
            _cmbParam = new ComboBox
            {
                Location = new Point(16, y),
                Width = 286,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var p in CzechData.AvailableParameters)
                _cmbParam.Items.Add(p);
            _cmbParam.SelectedIndex = 0;
            var paramDebounceTimer = new System.Windows.Forms.Timer { Interval = 500 };
            paramDebounceTimer.Tick += async (s, e) =>
            {
                paramDebounceTimer.Stop();
                if (GetSelectedModels().Count > 0)
                    await RunFetchAndProcessAsync(true);
            };
            _cmbParam.SelectedIndexChanged += (s, e) =>
            {
                UpdateDefaultWarningForParam();
                if (!_suppressAutoFetch)
                {
                    paramDebounceTimer.Stop();
                    paramDebounceTimer.Start();
                }
            };
            y += 30;

            _chkWarn = new CheckBox
            {
                Text = "Výstraha ≥",
                Location = new Point(16, y + 3),
                AutoSize = true,
                Checked = false
            };
            _numWarn = new NumericUpDown
            {
                Location = new Point(118, y),
                Width = 72,
                Minimum = -40,
                Maximum = 200,
                DecimalPlaces = 1,
                Value = 30.0m,
                Enabled = false
            };
            _chkWarn.CheckedChanged += (s, e) =>
            {
                _numWarn.Enabled = _chkWarn.Checked;
                RefreshWarningOverlay();
            };
            _numWarn.ValueChanged += (s, e) => RefreshWarningOverlay();
            y += 30;

            var lblAgg = new Label { Text = "Výpočet za kraj (tabulka):", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(16, y), AutoSize = true };
            y += 22;
            _cmbAggMode = new ComboBox
            {
                Location = new Point(16, y),
                Width = 286,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbAggMode.Items.AddRange(new object[]
            {
                "Průměr v kraji (ze sítě 220 bodů)",
                "Maximum v kraji (ze sítě 220 bodů)",
                "Krajské město (1 bod)"
            });
            _cmbAggMode.SelectedIndex = 0;
            _cmbAggMode.SelectedIndexChanged += (s, e) =>
            {
                if (_lastResult != null)
                {
                    PopulateGrid(_lastResult);
                    BuildSummaryMaps(_lastResult);
                    DisplaySelectedMap();
                }
            };
            y += 32;

            var lblHorizont = new Label { Text = "Období pro sumu:", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(16, y), AutoSize = true };
            y += 24;

            _rbDays = new RadioButton
            {
                Text = "Po dnech (horizont):",
                Location = new Point(16, y),
                AutoSize = true,
                Checked = true
            };
            _cmbDays = new ComboBox
            {
                Location = new Point(172, y - 2),
                Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbDays.Items.AddRange(new object[] { "1 den", "2 dny", "3 dny", "5 dní", "7 dní" });
            _cmbDays.SelectedIndex = 2; // 3 dny
            y += 28;

            _rbHours = new RadioButton
            {
                Text = "Vlastní úsek v hodinách:",
                Location = new Point(16, y),
                AutoSize = true
            };
            y += 26;

            DateTime nowHour = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
            var lblOd = new Label { Text = "Od:", Location = new Point(34, y + 3), AutoSize = true };
            _dtpStart = new DateTimePicker
            {
                Location = new Point(66, y),
                Width = 236,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy HH:00",
                Value = nowHour,
                Enabled = false
            };
            y += 28;

            var lblDo = new Label { Text = "Do:", Location = new Point(34, y + 3), AutoSize = true };
            _dtpEnd = new DateTimePicker
            {
                Location = new Point(66, y),
                Width = 236,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy HH:00",
                Value = nowHour.AddHours(24),
                Enabled = false
            };
            y += 26;

            _lblHourRange = new Label
            {
                Text = "Rozsah: 24 hodin",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.2f),
                Location = new Point(66, y),
                AutoSize = true
            };
            y += 24;

            _rbDays.CheckedChanged += (s, e) => UpdatePeriodModeUi();
            _rbHours.CheckedChanged += (s, e) => UpdatePeriodModeUi();
            _dtpStart.ValueChanged += (s, e) => UpdateHourRangeLabel();
            _dtpEnd.ValueChanged += (s, e) => UpdateHourRangeLabel();

            var lblModels = new Label { Text = "Meteorologické modely:", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(16, y), AutoSize = true };
            var lnkAll = new LinkLabel { Text = "Vše", Location = new Point(228, y + 2), AutoSize = true };
            var lnkNone = new LinkLabel { Text = "Nic", Location = new Point(268, y + 2), AutoSize = true };
            lnkAll.Click += (s, e) => SetAllModelsChecked(true);
            lnkNone.Click += (s, e) => SetAllModelsChecked(false);
            y += 24;

            _chkModels = new CheckedListBox
            {
                Location = new Point(16, y),
                Size = new Size(286, 175),
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            foreach (var m in CzechData.AvailableModels)
                _chkModels.Items.Add(m, true);
            y += 188;

            var lblApi = new Label { Text = "Meteologix API klíč (volitelné):", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(16, y), AutoSize = true };
            y += 24;
            _txtApiKey = new TextBox
            {
                Location = new Point(16, y),
                Width = 286,
                UseSystemPasswordChar = true
            };
            y += 30;
            var lblApiHint = new Label
            {
                Text = "Bez klíče se používá bezplatné Open-Meteo API.",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.2f),
                Location = new Point(16, y),
                AutoSize = true
            };
            y += 26;

            var lblOut = new Label { Text = "Složka pro snímky a tabulky:", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(16, y), AutoSize = true };
            y += 24;
            _txtOutputDir = new TextBox
            {
                Location = new Point(16, y),
                Width = 200,
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeteoBarbucha")
            };
            _btnBrowse = new Button
            {
                Text = "Vybrat...",
                Location = new Point(222, y - 1),
                Size = new Size(80, 26),
                FlatStyle = FlatStyle.Flat
            };
            _btnBrowse.Click += (s, e) => BrowseFolder();
            y += 42;

            _btnRun = new Button
            {
                Text = "Stáhnout data a spočítat",
                Location = new Point(16, y),
                Size = new Size(286, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRun.FlatAppearance.BorderSize = 0;
            _btnRun.Click += async (s, e) => await RunFetchAndProcessAsync();
            y += 46;

            _btnSaveAll = new Button
            {
                Text = "Uložit snímky a tabulku",
                Location = new Point(16, y),
                Size = new Size(286, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Enabled = false,
                Cursor = Cursors.Hand
            };
            _btnSaveAll.FlatAppearance.BorderSize = 0;
            _btnSaveAll.Click += (s, e) => SaveAllResults(true);
            y += 46;

            _chkAutoRun = new CheckBox
            {
                Text = "Auto-stahovat a ukládat každých:",
                Location = new Point(16, y + 3),
                AutoSize = true
            };
            _cmbAutoHours = new ComboBox
            {
                Location = new Point(234, y),
                Width = 68,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbAutoHours.Items.AddRange(new object[] { "1 h", "3 h", "6 h", "12 h" });
            _cmbAutoHours.SelectedIndex = 1; // 3 h

            _autoTimer = new System.Windows.Forms.Timer();
            _autoTimer.Tick += async (s, e) =>
            {
                AppendLog("Spouštím automatické stažení a uložení...");
                await RunFetchAndProcessAsync();
                if (_lastResult != null)
                    SaveAllResults(false);
            };
            _chkAutoRun.CheckedChanged += (s, e) => UpdateAutoTimer();
            _cmbAutoHours.SelectedIndexChanged += (s, e) => UpdateAutoTimer();

            sidebar.Controls.AddRange(new Control[]
            {
                lblParam, _cmbParam, _chkWarn, _numWarn,
                lblAgg, _cmbAggMode,
                lblHorizont, _rbDays, _cmbDays, _rbHours, lblOd, _dtpStart, lblDo, _dtpEnd, _lblHourRange,
                lblModels, lnkAll, lnkNone, _chkModels,
                lblApi, _txtApiKey, lblApiHint,
                lblOut, _txtOutputDir, _btnBrowse,
                _btnRun, _btnSaveAll, _chkAutoRun, _cmbAutoHours
            });

            // Main TabControl
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14)
            };

            // Timeline bar above TabControl
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
            _trkTimeStep = new TrackBar
            {
                Location = new Point(95, 3),
                Width = 420,
                Minimum = -1,
                Maximum = -1,
                Value = -1,
                TickStyle = TickStyle.None,
                Enabled = false
            };
            _lblTimeStep = new Label
            {
                Text = "Celé období (souhrn)",
                ForeColor = Color.FromArgb(37, 99, 235),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(525, 10),
                AutoSize = true
            };
            _trkTimeStep.ValueChanged += (s, e) => OnTimeStepChanged();
            timelineBar.Controls.Add(lblTimeTitle);
            timelineBar.Controls.Add(_trkTimeStep);
            timelineBar.Controls.Add(_lblTimeStep);

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };
            _tabs = tabs;

            // Tab 1: Table
            var tabTable = new TabPage("Tabulka hodnot (kraje × modely)");
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = 36
            };
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            tabTable.Controls.Add(_grid);

            // Tab 2: Map Snapshots (Multi-View + Zoomable Detail View)
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

            _lblSelectMap = new Label { Text = "Detail modelu:", Location = new Point(175, 13), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Enabled = false };
            _cmbMapSelect = new ComboBox { Location = new Point(268, 10), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            _cmbMapSelect.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbMapSelect.SelectedItem != null && _picMap.Visible)
                    DisplaySelectedMap();
            };

            _btnResetZoom = new Button
            {
                Text = "Resetovat zoom",
                Location = new Point(518, 9),
                Size = new Size(118, 28),
                FlatStyle = FlatStyle.Flat,
                Visible = false
            };
            _lblZoomHint = new Label
            {
                Text = "Klikněte na mapu v Multi-view pro detail a zoom",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(645, 14),
                AutoSize = true
            };

            mapTop.Controls.Add(_btnBackToMultiView);
            mapTop.Controls.Add(_lblSelectMap);
            mapTop.Controls.Add(_cmbMapSelect);
            mapTop.Controls.Add(_btnResetZoom);
            mapTop.Controls.Add(_lblZoomHint);

            _picMap = new ZoomablePictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 248, 252),
                Visible = false
            };
            _picMap.ImageMouseMoved += OnMapMouseMoved;
            _picMap.ImageClicked += pt => OpenMeteogramAtPixel(pt);
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

            // Tab 3: Výběr oblast (Synchronized Multi-View + Lupa +/-)
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
                Text = "Zkratky: + / − (lupa), šipky (posun), 0 (reset) | Tažení myší synchronizuje všechny modely",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(598, 14),
                AutoSize = true
            };

            areaTop.Controls.Add(lblArea);
            areaTop.Controls.Add(_cmbAreaPreset);
            areaTop.Controls.Add(btnZoomIn);
            areaTop.Controls.Add(btnZoomOut);
            areaTop.Controls.Add(btnAreaReset);
            areaTop.Controls.Add(lblAreaHint);

            _areaMultiViewPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(10)
            };

            _tabArea.Controls.Add(_areaMultiViewPanel);
            _tabArea.Controls.Add(areaTop);

            // Tab 4: Log
            var tabLog = new TabPage("Log průběhu");
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

            tabs.TabPages.Add(tabTable);
            tabs.TabPages.Add(tabMaps);
            tabs.TabPages.Add(_tabArea);
            tabs.TabPages.Add(tabLog);
            mainPanel.Controls.Add(tabs);
            mainPanel.Controls.Add(timelineBar);

            // StatusStrip
            var statusStrip = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel("Připraveno. Klikněte na 'Stáhnout data a spočítat'.")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _progress = new ToolStripProgressBar { Width = 160, Style = ProgressBarStyle.Marquee, Visible = false };
            var lblVersion = new ToolStripStatusLabel("Verze " + UpdateService.CurrentVersion + "  |  Vydáno: " + UpdateService.ReleaseDate)
            {
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85),
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                Padding = new Padding(10, 0, 6, 0)
            };
            statusStrip.Items.Add(_lblStatus);
            statusStrip.Items.Add(_progress);
            statusStrip.Items.Add(lblVersion);

            Controls.Add(mainPanel);
            Controls.Add(sidebar);
            Controls.Add(header);
            Controls.Add(statusStrip);
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
                _lblHourRange.Text = "Režim: celý denní horizont";
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

        private void BrowseFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Vyberte složku pro ukládání snímků srážek a CSV tabulek";
                dlg.SelectedPath = _txtOutputDir.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtOutputDir.Text = dlg.SelectedPath;
            }
        }

        private void OpenOutputFolder()
        {
            string dir = _txtOutputDir.Text.Trim();
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
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            _txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
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
            if (!_chkAutoRun.Checked)
            {
                AppendLog("Automatické stahování vypnuto.");
                return;
            }
            int hours = 3;
            switch (_cmbAutoHours.SelectedIndex)
            {
                case 0: hours = 1; break;
                case 1: hours = 3; break;
                case 2: hours = 6; break;
                case 3: hours = 12; break;
            }
            _autoTimer.Interval = hours * 3600 * 1000;
            _autoTimer.Start();
            AppendLog("Automatické stahování zapnuto (každých " + hours + " h).");
        }

        private string BuildCacheKey(List<WeatherModelInfo> models, int days, DateTime? startHour, DateTime? endHour, WeatherParameter param, string apiKey)
        {
            var ids = new List<string>();
            foreach (var m in models) ids.Add(m.Id);
            string period = (startHour.HasValue && endHour.HasValue)
                ? startHour.Value.ToString("yyyyMMddHH") + "-" + endHour.Value.ToString("yyyyMMddHH")
                : "D" + days;
            return param + "|" + period + "|" + string.Join(",", ids) + "|" + (apiKey ?? "").Trim();
        }

        private void UpdateDefaultWarningForParam()
        {
            var p = GetSelectedParameter();
            _suppressAutoFetch = true;
            try
            {
                switch (p)
                {
                    case WeatherParameter.UtciMax:
                        _chkWarn.Checked = true;
                        _numWarn.Value = 32.0m;
                        break;
                    case WeatherParameter.WindGusts10m:
                    case WeatherParameter.WindSpeed10m:
                        _numWarn.Value = 70.0m;
                        break;
                    case WeatherParameter.TotalPrecipitation:
                        _numWarn.Value = 30.0m;
                        break;
                    case WeatherParameter.Snowfall:
                        _numWarn.Value = 15.0m;
                        break;
                    default:
                        _numWarn.Value = 30.0m;
                        break;
                }
                _numWarn.Enabled = _chkWarn.Checked;
            }
            finally
            {
                _suppressAutoFetch = false;
            }
        }

        private void RefreshWarningOverlay()
        {
            if (_suppressAutoFetch || _lastResult == null) return;
            _lastResult.WarningThreshold = _chkWarn.Checked ? (double?)_numWarn.Value : null;
            PopulateGrid(_lastResult);
            BuildSummaryMaps(_lastResult);
        }

        private void SetupTimelineForResult(ForecastResult result)
        {
            _suppressAutoFetch = true;
            try
            {
                if (result != null && result.TimeSteps != null && result.TimeSteps.Count > 1)
                {
                    _trkTimeStep.Minimum = -1;
                    _trkTimeStep.Maximum = result.TimeSteps.Count - 1;
                    _trkTimeStep.Value = Math.Max(-1, Math.Min(result.TimeSteps.Count - 1, result.SelectedTimeStepIndex));
                    _trkTimeStep.Enabled = true;
                }
                else
                {
                    _trkTimeStep.Minimum = -1;
                    _trkTimeStep.Maximum = -1;
                    _trkTimeStep.Value = -1;
                    _trkTimeStep.Enabled = false;
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
                _lblTimeStep.Text = "Celé období (souhrn)";
            else
                _lblTimeStep.Text = "Krok: " + _lastResult.TimeSteps[_trkTimeStep.Value].Replace("T", " ");
        }

        private void OnTimeStepChanged()
        {
            if (_suppressAutoFetch || _lastResult == null) return;
            _lastResult.SelectedTimeStepIndex = _trkTimeStep.Value;
            UpdateTimeStepLabel();
            PopulateGrid(_lastResult);
            BuildSummaryMaps(_lastResult);
            if (_picMap.Visible)
                DisplaySelectedMap();
        }

        private async System.Threading.Tasks.Task RunFetchAndProcessAsync(bool useCache = false)
        {
            var models = GetSelectedModels();
            if (models.Count == 0)
            {
                MessageBox.Show(this, "Vyberte alespoň jeden meteorologický model.", "Upozornění", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_fetchCts != null)
            {
                _fetchCts.Cancel();
                _fetchCts.Dispose();
            }
            _fetchCts = new CancellationTokenSource();
            var ct = _fetchCts.Token;

            _btnSaveAll.Enabled = false;
            _progress.Visible = true;
            _lblStatus.Text = "Stahuji data a počítám hodnoty...";

            try
            {
                int days = GetSelectedDays();
                string apiKey = _txtApiKey.Text;
                DateTime? startHour = _rbHours.Checked ? (DateTime?)_dtpStart.Value : null;
                DateTime? endHour = _rbHours.Checked ? (DateTime?)_dtpEnd.Value : null;
                WeatherParameter param = GetSelectedParameter();

                string cacheKey = BuildCacheKey(models, days, startHour, endHour, param, apiKey);
                ForecastResult cached;
                if (useCache && _resultCache.TryGetValue(cacheKey, out cached))
                {
                    _lastResult = cached;
                    AppendLog("Načteno z mezipaměti (bez čekání): " + CzechData.GetParameterInfo(param).DisplayName);
                }
                else
                {
                    _lastResult = await _weatherService.FetchPrecipitationAsync(models, days, startHour, endHour, param, apiKey, AppendLog, ct);
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

                _lastResult.WarningThreshold = _chkWarn.Checked ? (double?)_numWarn.Value : null;
                _lastResult.SelectedTimeStepIndex = -1;
                SetupTimelineForResult(_lastResult);
                PopulateGrid(_lastResult);
                BuildSummaryMaps(_lastResult);

                _btnSaveAll.Enabled = true;
                _lblStatus.Text = "Hotovo (" + _lastResult.PeriodDescription + "). Kliknutím na mapu zobrazíte meteogram.";
            }
            catch (OperationCanceledException)
            {
                AppendLog("Předchozí stahování zrušeno novějším požadavkem.");
            }
            catch (Exception ex)
            {
                AppendLog("CHYBA: " + ex.Message);
                _lblStatus.Text = "Chyba při stahování dat.";
                MessageBox.Show(this, "Nastala chyba při stahování nebo zpracování dat:\n\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnRun.Enabled = true;
                _progress.Visible = false;
            }
        }

        private void OnMapMouseMoved(PointF? imgPt)
        {
            if (_cmbMapSelect.SelectedItem == null) return;
            OnAreaMapMouseMoved(_picMap, _cmbMapSelect.SelectedItem.ToString(), imgPt);
        }

        private double? LookupPointValue(GeoGridPoint nearest, string modelKey)
        {
            if (nearest == null || _lastResult == null) return null;
            int step = _lastResult.SelectedTimeStepIndex;
            if (modelKey == "prumer_modelu" || modelKey == "max_modelu" || modelKey == "rozptyl_modelu")
            {
                double sum = 0, min = double.MaxValue, max = double.MinValue;
                int cnt = 0;
                foreach (var m in _lastResult.Models)
                {
                    double? mv = WeatherService.GetGridPointValue(nearest, m.Id, step);
                    if (mv.HasValue)
                    {
                        sum += mv.Value;
                        if (mv.Value < min) min = mv.Value;
                        if (mv.Value > max) max = mv.Value;
                        cnt++;
                    }
                }
                if (cnt == 0) return null;
                return modelKey == "prumer_modelu" ? (sum / cnt) : (modelKey == "max_modelu" ? max : (max - min));
            }

            return WeatherService.GetGridPointValue(nearest, modelKey, step);
        }

        private void OpenMeteogramAtPixel(PointF imgPt)
        {
            if (_lastResult == null || _lastResult.GridPoints == null || _lastResult.GridPoints.Count == 0)
                return;

            double lat, lon;
            if (!MapRenderer.TryImagePixelToLatLon(imgPt, out lat, out lon))
                return;

            string regionCode = CzechData.FindRegionCode(lat, lon);
            if (string.IsNullOrEmpty(regionCode))
                return;

            GeoGridPoint nearest = null;
            double bestD2 = double.MaxValue;
            foreach (var gp in _lastResult.GridPoints)
            {
                double d2 = (gp.Latitude - lat) * (gp.Latitude - lat) + (gp.Longitude - lon) * (gp.Longitude - lon);
                if (d2 < bestD2) { bestD2 = d2; nearest = gp; }
            }
            if (nearest == null) return;

            using (var dlg = new MeteogramDialog(_lastResult, nearest, regionCode))
                dlg.ShowDialog(this);
        }

        private void ApplySavedSettings()
        {
            var s = SettingsManager.Load();
            if (s == null) return;

            _suppressAutoFetch = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(s.OutputDir))
                    _txtOutputDir.Text = s.OutputDir;
                if (!string.IsNullOrEmpty(s.MeteologixApiKey))
                    _txtApiKey.Text = s.MeteologixApiKey;
                if (!string.IsNullOrWhiteSpace(s.UpdateManifestUrl))
                    _updateManifestUrl = s.UpdateManifestUrl;
                if (s.ParamIndex >= 0 && s.ParamIndex < _cmbParam.Items.Count)
                    _cmbParam.SelectedIndex = s.ParamIndex;
                if (s.AggModeIndex >= 0 && s.AggModeIndex < _cmbAggMode.Items.Count)
                    _cmbAggMode.SelectedIndex = s.AggModeIndex;
                if (s.ForecastDaysIndex >= 0 && s.ForecastDaysIndex < _cmbDays.Items.Count)
                    _cmbDays.SelectedIndex = s.ForecastDaysIndex;
                if (s.AutoHoursIndex >= 0 && s.AutoHoursIndex < _cmbAutoHours.Items.Count)
                    _cmbAutoHours.SelectedIndex = s.AutoHoursIndex;
                if (s.UtciWarningThreshold >= -40 && s.UtciWarningThreshold <= 200)
                    _numWarn.Value = (decimal)s.UtciWarningThreshold;

                if (s.SelectedModelIds != null && s.SelectedModelIds.Count > 0)
                {
                    var set = new HashSet<string>(s.SelectedModelIds);
                    for (int i = 0; i < _chkModels.Items.Count; i++)
                    {
                        var m = _chkModels.Items[i] as WeatherModelInfo;
                        if (m != null)
                            _chkModels.SetItemChecked(i, set.Contains(m.Id));
                    }
                }
            }
            finally
            {
                _suppressAutoFetch = false;
            }
        }

        private void SaveCurrentSettings()
        {
            var s = new AppSettings
            {
                OutputDir = _txtOutputDir.Text.Trim(),
                MeteologixApiKey = _txtApiKey.Text,
                UpdateManifestUrl = _updateManifestUrl,
                ParamIndex = _cmbParam.SelectedIndex,
                AggModeIndex = _cmbAggMode.SelectedIndex,
                ForecastDaysIndex = _cmbDays.SelectedIndex,
                AutoHoursIndex = _cmbAutoHours.SelectedIndex,
                UtciWarningThreshold = (double)_numWarn.Value
            };
            foreach (var m in GetSelectedModels())
                s.SelectedModelIds.Add(m.Id);

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
                // Silent on startup if offline or unreachable
            }
        }

        private void BuildSummaryMaps(ForecastResult result)
        {
            var regionalTable = WeatherService.ComputeRegionalTable(result, GetSelectedAggMode());

            _modelMapsData.Clear();
            _modelMapKeys.Clear();
            _cmbMapSelect.Items.Clear();

            foreach (var model in result.Models)
            {
                var modelValues = new Dictionary<string, double?>();
                foreach (var r in CzechData.Regions)
                {
                    double? v;
                    regionalTable[r.Code].TryGetValue(model.Id, out v);
                    modelValues[r.Code] = v;
                }
                _modelMapsData[model.DisplayName] = modelValues;
                _modelMapKeys[model.DisplayName] = model.Id;
                _cmbMapSelect.Items.Add(model.DisplayName);
            }

            var avgValues = new Dictionary<string, double?>();
            var maxValues = new Dictionary<string, double?>();
            var spreadValues = new Dictionary<string, double?>();

            foreach (var r in CzechData.Regions)
            {
                double sum = 0;
                double min = double.MaxValue;
                double max = double.MinValue;
                int cnt = 0;
                foreach (var m in result.Models)
                {
                    double? v;
                    regionalTable[r.Code].TryGetValue(m.Id, out v);
                    if (v.HasValue)
                    {
                        sum += v.Value;
                        if (v.Value < min) min = v.Value;
                        if (v.Value > max) max = v.Value;
                        cnt++;
                    }
                }
                avgValues[r.Code] = cnt > 0 ? (double?)Math.Round(sum / cnt, 2) : null;
                maxValues[r.Code] = cnt > 0 ? (double?)Math.Round(max, 2) : null;
                spreadValues[r.Code] = cnt > 0 ? (double?)Math.Round(max - min, 2) : null;
            }

            string avgName = "Průměr všech modelů";
            string maxName = "Maximum ze všech modelů";
            string spreadName = "Rozptyl modelů (Max - Min)";

            _modelMapsData[avgName] = avgValues;
            _modelMapKeys[avgName] = "prumer_modelu";

            _modelMapsData[maxName] = maxValues;
            _modelMapKeys[maxName] = "max_modelu";

            _modelMapsData[spreadName] = spreadValues;
            _modelMapKeys[spreadName] = "rozptyl_modelu";

            _cmbMapSelect.Items.Insert(0, spreadName);
            _cmbMapSelect.Items.Insert(0, maxName);
            _cmbMapSelect.Items.Insert(0, avgName);
            _cmbMapSelect.SelectedIndex = 0;

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

            // Snapshot inputs for background rendering
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
                var list = new List<Tuple<string, Bitmap>>();
                foreach (var job in renderJobs)
                {
                    Bitmap bmp = MapRenderer.RenderMapBitmap(job.Item2, job.Item1, resultRef, job.Item3);
                    list.Add(Tuple.Create(job.Item1, bmp));
                }
                return list;
            });

            if (_lastResult != resultRef)
            {
                foreach (var r in rendered) r.Item2.Dispose();
                _multiViewPanel.ResumeLayout();
                _areaMultiViewPanel.ResumeLayout();
                return;
            }

            foreach (var entry in rendered)
            {
                string modelName = entry.Item1;
                Bitmap fullBmp = entry.Item2;
                _multiViewThumbnails.Add(fullBmp);

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
                    BackColor = Color.FromArgb(15, 23, 42),
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

                // Synchronized card in "Výběr oblast" tab
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
                    BackColor = Color.FromArgb(15, 23, 42),
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
                areaPic.ImageClicked += pt => OpenMeteogramAtPixel(pt);

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
            // Normalized pan offsets relative to 1200x760 Czechia map projection
            float zoom = 1.0f;
            PointF pan = PointF.Empty;
            switch (index)
            {
                case 1: zoom = 2.6f; pan = new PointF(0.11f, 0.04f); break;   // Praha a Střední Čechy
                case 2: zoom = 2.5f; pan = new PointF(0.11f, -0.21f); break;  // Šumava / Jižní Čechy
                case 3: zoom = 2.6f; pan = new PointF(0.28f, 0.00f); break;   // Západní Čechy
                case 4: zoom = 2.6f; pan = new PointF(0.20f, 0.19f); break;   // Krušné hory / Ústecko
                case 5: zoom = 2.8f; pan = new PointF(0.01f, 0.22f); break;   // Krkonoše / Liberecko
                case 6: zoom = 2.5f; pan = new PointF(-0.07f, 0.08f); break;  // Východní Čechy
                case 7: zoom = 2.6f; pan = new PointF(-0.03f, -0.09f); break; // Vysočina
                case 8: zoom = 2.5f; pan = new PointF(-0.14f, -0.17f); break; // Jižní Morava
                case 9: zoom = 2.6f; pan = new PointF(-0.22f, 0.06f); break;  // Jeseníky / Olomoucko
                case 10: zoom = 2.7f; pan = new PointF(-0.33f, 0.02f); break; // Beskydy / Ostravsko
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
            double? val = LookupPointValue(nearest, modelKey);
            string valText = !val.HasValue
                ? "N/A"
                : (_lastResult.Parameter == WeatherParameter.SignificantWeather
                    ? CzechData.GetWmoShortLabel(val.Value) + " (" + ((int)Math.Round(val.Value)) + ")"
                    : val.Value.ToString("0.0", CultureInfo.InvariantCulture) + " " + _lastResult.Unit);
            bool isWarn = _lastResult.WarningThreshold.HasValue &&
                          val.HasValue &&
                          val.Value >= _lastResult.WarningThreshold.Value;
            string overlayText = (isWarn ? "⚠ " : "") + valText + " (" + regionCode + ")" + (isWarn ? " — VÝSTRAHA!" : "");
            if (targetBox != null)
                targetBox.SetHoverOverlay(overlayText, targetBox.PointToClient(Cursor.Position));
            _lblStatus.Text = string.Format(CultureInfo.InvariantCulture, "Kurzor: {0:0.00}° N, {1:0.00}° E ({2})  |  {3}: {4}{5}", lat, lon, regionCode, modelName, valText, isWarn ? "  [⚠ VÝSTRAHA]" : "");
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_tabs != null && _tabs.SelectedTab == _tabArea && !(ActiveControl is TextBox))
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
                else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
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
            _lblZoomHint.Text = "Kolečko: zoom | Tažení myší: posun | Dvojklik: reset";

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

        private void SaveAllResults(bool showDialog)
        {
            if (_lastResult == null || _modelMapsData.Count == 0)
                return;

            string baseDir = _txtOutputDir.Text.Trim();
            if (string.IsNullOrEmpty(baseDir))
            {
                MessageBox.Show(this, "Zadejte platnou výstupní složku.", "Upozornění", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string stamp = _lastResult.FetchedAt.ToString("yyyy-MM-dd_HH-mm-ss");
                string prefix = CzechData.GetParameterInfo(_lastResult.Parameter).FilePrefix;
                string snapshotsDir = Path.Combine(baseDir, "snimky", prefix + "_" + stamp);
                string csvPath = Path.Combine(baseDir, "tabulky", prefix + "_kraje_" + stamp + ".csv");

                int savedCount = 0;
                foreach (var kvp in _modelMapsData)
                {
                    string fileKey = _modelMapKeys[kvp.Key];
                    string pngPath = MapRenderer.SaveModelMapSnapshot(snapshotsDir, fileKey, kvp.Key, _lastResult, kvp.Value);
                    AppendLog("Uložen snímek: " + pngPath);
                    savedCount++;
                }

                CsvExporter.ExportToCsv(csvPath, _lastResult, GetSelectedAggMode());
                AppendLog("Uložena CSV tabulka: " + csvPath);

                _lblStatus.Text = "Uloženo " + savedCount + " snímků a CSV tabulka (" + stamp + ").";
                if (showDialog)
                    MessageBox.Show(this, "Uloženo " + savedCount + " mapových snímků a CSV tabulka do složky:\n" + baseDir, "Uložení dokončeno", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog("CHYBA při ukládání: " + ex.Message);
                if (showDialog)
                    MessageBox.Show(this, "Chyba při ukládání souborů:\n\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateGrid(ForecastResult result)
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();

            string unit = string.IsNullOrEmpty(result.Unit) ? "mm" : result.Unit;
            var regionalTable = WeatherService.ComputeRegionalTable(result, GetSelectedAggMode());

            _grid.Columns.Add("Code", "Kód");
            _grid.Columns["Code"].Width = 55;
            _grid.Columns["Code"].FillWeight = 35;

            _grid.Columns.Add("Name", "Kraj");
            _grid.Columns["Name"].FillWeight = 120;

            foreach (var m in result.Models)
            {
                var col = new DataGridViewTextBoxColumn
                {
                    Name = m.Id,
                    HeaderText = m.DisplayName + "\n(" + unit + ")",
                    FillWeight = 70
                };
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                _grid.Columns.Add(col);
            }

            var avgCol = new DataGridViewTextBoxColumn
            {
                Name = "Avg",
                HeaderText = "Průměr\n(" + unit + ")",
                FillWeight = 70
            };
            avgCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            avgCol.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _grid.Columns.Add(avgCol);

            var modelTotals = new Dictionary<string, double>();
            var modelCounts = new Dictionary<string, int>();
            foreach (var m in result.Models)
            {
                modelTotals[m.Id] = 0;
                modelCounts[m.Id] = 0;
            }

            foreach (var r in CzechData.Regions)
            {
                int rowIdx = _grid.Rows.Add();
                var row = _grid.Rows[rowIdx];
                row.Cells["Code"].Value = r.Code;
                row.Cells["Name"].Value = r.Name;

                double rowSum = 0;
                int rowCnt = 0;

                foreach (var m in result.Models)
                {
                    double? val;
                    regionalTable[r.Code].TryGetValue(m.Id, out val);
                    if (val.HasValue)
                    {
                        row.Cells[m.Id].Value = result.Parameter == WeatherParameter.SignificantWeather
                            ? ((int)Math.Round(val.Value)) + " — " + CzechData.GetWmoShortLabel(val.Value)
                            : val.Value.ToString("0.0", CultureInfo.InvariantCulture);
                        row.Cells[m.Id].Style.BackColor = MapRenderer.GetValueColor(val.Value, result.Parameter);
                        rowSum += val.Value;
                        rowCnt++;
                        modelTotals[m.Id] += val.Value;
                        modelCounts[m.Id]++;
                    }
                    else
                    {
                        row.Cells[m.Id].Value = "—";
                        row.Cells[m.Id].Style.ForeColor = Color.Gray;
                    }
                }

                if (rowCnt > 0)
                {
                    double avg = rowSum / rowCnt;
                    row.Cells["Avg"].Value = avg.ToString("0.0", CultureInfo.InvariantCulture);
                    row.Cells["Avg"].Style.BackColor = MapRenderer.GetValueColor(avg, result.Parameter);
                }
            }

            // Summary row: Průměr ČR
            int sumIdx = _grid.Rows.Add();
            var sumRow = _grid.Rows[sumIdx];
            sumRow.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            sumRow.DefaultCellStyle.BackColor = Color.FromArgb(226, 232, 240);
            sumRow.Cells["Code"].Value = "ČR";
            sumRow.Cells["Name"].Value = "Průměr za ČR";

            double grandSum = 0;
            int grandCnt = 0;
            foreach (var m in result.Models)
            {
                if (modelCounts[m.Id] > 0)
                {
                    double avg = modelTotals[m.Id] / modelCounts[m.Id];
                    sumRow.Cells[m.Id].Value = avg.ToString("0.0", CultureInfo.InvariantCulture);
                    grandSum += avg;
                    grandCnt++;
                }
            }
            if (grandCnt > 0)
            {
                sumRow.Cells["Avg"].Value = (grandSum / grandCnt).ToString("0.0", CultureInfo.InvariantCulture);
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
