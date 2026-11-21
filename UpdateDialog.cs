using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class UpdateDialog : Form
    {
        private readonly TextBox _txtUrl;
        private readonly Label _lblStatus;
        private readonly TextBox _txtNotes;
        private readonly Button _btnCheck;
        private readonly Button _btnInstall;
        private UpdateInfo _lastUpdate;

        public string ManifestUrl { get { return _txtUrl.Text.Trim(); } }

        public UpdateDialog(string currentManifestUrl)
        {
            Text = "Aktualizace aplikace — MeteoBarbucha ČR";
            Size = new Size(600, 420);
            MinimumSize = new Size(520, 360);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            int y = 18;
            var lblVer = new Label
            {
                Text = "Aktuální verze aplikace: " + UpdateService.CurrentVersion,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 34;

            var lblUrl = new Label
            {
                Text = "URL aktualizačního manifestu (version.json nebo GitHub /releases/latest):",
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 24;

            _txtUrl = new TextBox
            {
                Location = new Point(20, y),
                Width = 540,
                Text = currentManifestUrl ?? ""
            };
            y += 32;

            var lblHint = new Label
            {
                Text = "Příklad: https://api.github.com/repos/UZIVATEL/MeteoBarbucha/releases/latest",
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.3f),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 28;

            _btnCheck = new Button
            {
                Text = "Zkontrolovat aktualizace",
                Location = new Point(20, y),
                Size = new Size(195, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCheck.FlatAppearance.BorderSize = 0;
            _btnCheck.Click += async (s, e) => await CheckAsync();

            _btnInstall = new Button
            {
                Text = "Stáhnout a aktualizovat",
                Location = new Point(225, y),
                Size = new Size(195, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Enabled = false,
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += async (s, e) => await InstallAsync();
            y += 46;

            _lblStatus = new Label
            {
                Text = "Zadejte URL a klikněte na 'Zkontrolovat aktualizace'.",
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(20, y),
                Size = new Size(540, 22)
            };
            y += 26;

            _txtNotes = new TextBox
            {
                Location = new Point(20, y),
                Size = new Size(540, 110),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var btnClose = new Button
            {
                Text = "Zavřít",
                Location = new Point(460, 336),
                Size = new Size(100, 32),
                FlatStyle = FlatStyle.Flat
            };
            btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblVer, lblUrl, _txtUrl, lblHint, _btnCheck, _btnInstall, _lblStatus, _txtNotes, btnClose });
        }

        private async System.Threading.Tasks.Task CheckAsync()
        {
            _btnCheck.Enabled = false;
            _btnInstall.Enabled = false;
            _lblStatus.Text = "Kontroluji dostupnost nové verze...";
            _txtNotes.Text = "";

            try
            {
                _lastUpdate = await UpdateService.CheckForUpdateAsync(_txtUrl.Text);
                if (_lastUpdate.IsNewer)
                {
                    _lblStatus.Text = "Je dostupná nová verze " + _lastUpdate.Version + "!";
                    _lblStatus.ForeColor = Color.FromArgb(5, 150, 105);
                    _txtNotes.Text = string.IsNullOrEmpty(_lastUpdate.Notes) ? "Nová verze je připravena ke stažení." : _lastUpdate.Notes;
                    _btnInstall.Enabled = true;
                }
                else
                {
                    _lblStatus.Text = "Používáte nejnovější verzi (" + UpdateService.CurrentVersion + ").";
                    _lblStatus.ForeColor = Color.FromArgb(37, 99, 235);
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Chyba kontroly: " + ex.Message;
                _lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
            }
            finally
            {
                _btnCheck.Enabled = true;
            }
        }

        private async System.Threading.Tasks.Task InstallAsync()
        {
            if (_lastUpdate == null || string.IsNullOrEmpty(_lastUpdate.DownloadUrl))
                return;

            _btnCheck.Enabled = false;
            _btnInstall.Enabled = false;
            _lblStatus.Text = "Stahuji novou verzi a připravuji restart aplikace...";

            try
            {
                await UpdateService.DownloadAndApplyUpdateAsync(_lastUpdate.DownloadUrl);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Chyba aktualizace: " + ex.Message;
                _lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
                _btnCheck.Enabled = true;
                _btnInstall.Enabled = true;
            }
        }
    }
}
