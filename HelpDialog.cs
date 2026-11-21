using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class HelpDialog : Form
    {
        public HelpDialog()
        {
            Text = "Návod k použití — MeteoBarbucha ČR";
            Size = new Size(780, 640);
            MinimumSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.White;

            var rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Padding = new Padding(16),
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            PopulateContent(rtb);

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = Color.FromArgb(241, 245, 249)
            };

            var btnClose = new Button
            {
                Text = "Zavřít návod",
                Size = new Size(130, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(bottomPanel.Width - 148, 9),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            bottomPanel.Controls.Add(btnClose);

            var wrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 10) };
            wrapper.Controls.Add(rtb);

            Controls.Add(wrapper);
            Controls.Add(bottomPanel);
        }

        private static void PopulateContent(RichTextBox rtb)
        {
            AppendHeading(rtb, "MeteoBarbucha ČR — Návod k použití", 15f);
            AppendText(rtb, "Aplikace stahuje předpovědi srážek a sněžení pro všech 14 krajů České republiky napříč numerickými meteorologickými modely, počítá součty za zvolené období a generuje přehledné mapy i CSV tabulky.\n\n");

            AppendHeading(rtb, "1. Rychlý postup (krok za krokem)", 11.5f);
            AppendText(rtb,
                "1) V levém panelu vyberte Parametr předpovědi (Celkové srážky v mm nebo Sníh / Sněžení v cm).\n" +
                "2) Zvolte způsob výpočtu za kraj (Průměr v kraji, Maximum v kraji nebo Krajské město).\n" +
                "3) Vyberte období — buď Po dnech (1–7 dní), nebo Vlastní úsek v hodinách (přesné Od – Do).\n" +
                "4) Zaškrtněte požadované meteorologické modely.\n" +
                "5) Klikněte na tlačítko „Stáhnout data a spočítat“.\n" +
                "6) Prohlédněte si výsledky v záložce Tabulka srážek nebo Snímky srážek (Mapy ČR).\n" +
                "7) Pro uložení na disk klikněte na „Uložit snímky a tabulku“.\n\n");

            AppendHeading(rtb, "2. Výpočet za kraj (220bodová prostorová síť)", 11.5f);
            AppendText(rtb,
                "Aplikace kromě 14 krajských měst stahuje také prostorovou síť 220 bodů pokrývajících celou ČR:\n" +
                "• Průměr v kraji — aritmetický průměr všech bodů sítě ležících v daném kraji.\n" +
                "• Maximum v kraji — nejvyšší hodnota ze všech bodů sítě v daném kraji (vhodné pro hlídání horských maxim a přívalových srážek).\n" +
                "• Krajské město — bodová hodnota přímo pro souřadnice krajského města.\n" +
                "Přepnutí režimu ihned přepočítá tabulku bez nutnosti znovu stahovat data.\n\n");

            AppendHeading(rtb, "3. Dostupné meteorologické modely", 11.5f);
            AppendText(rtb,
                "• Otevřené modely (Open-Meteo API, zdarma bez klíče): ALADIN CZ (1 km, ČHMÚ), ECMWF IFS 0.25°, ICON-EU, ICON-D2 (2.2 km), ICON-D2 RUC (DWD), GFS Global, GEM Global, JMA Seamless, UKMO Seamless, ARPEGE Europe.\n" +
                "• Volitelné Meteologix / Kachelmannwetter API: Po zadání vlastního X-API-Key klíče se automaticky dotážou i modely Meteologix ECMWF, Europe Swiss 4x4 a Europe Swiss Nowcast HD.\n\n");

            AppendHeading(rtb, "4. Práce s mapou (Zoom, posun a souhrnné mapy)", 11.5f);
            AppendText(rtb,
                "• Mapa vykresluje reálné hranice ČR, hranice 14 krajů, světle šedé hranice ORP a pozice krajských měst.\n" +
                "• V rozbalovacím seznamu nad mapou najdete kromě jednotlivých modelů také 3 souhrnné mapy: Průměr všech modelů, Maximum ze všech modelů a Rozptyl modelů (Max − Min).\n" +
                "• Ovládání mapy: kolečko myši = přiblížení/oddálení (zoom), tažení levou myší = posun mapy, dvojklik nebo tlačítko „Resetovat zoom“ = návrat na celé zobrazení.\n\n");

            AppendHeading(rtb, "5. Ukládání výstupů a automatický režim", 11.5f);
            AppendText(rtb,
                "• Tlačítko „Uložit snímky a tabulku“ uloží PNG mapy všech modelů do podsložky snimky\\ a souhrnnou CSV tabulku (UTF-8 s BOM, oddělovač středník pro český Excel) do podsložky tabulky\\.\n" +
                "• Zaškrtnutím „Auto-stahovat a ukládat každých X h“ bude aplikace pravidelně na pozadí stahovat nová data a automaticky je archivovat.\n\n");

            AppendHeading(rtb, "6. Přenositelnost a podpora", 11.5f);
            AppendText(rtb,
                "Aplikace je plně přenositelná (single-file .exe pro Windows 10/11 s .NET Framework 4.x). Nápady a připomínky můžete poslat přes tlačítko „Máte návrh na změnu aplikace?“ v horní liště.");
        }

        private static void AppendHeading(RichTextBox rtb, string text, float size)
        {
            rtb.SelectionFont = new Font("Segoe UI", size, FontStyle.Bold);
            rtb.SelectionColor = Color.FromArgb(15, 23, 42);
            rtb.AppendText(text + "\n");
        }

        private static void AppendText(RichTextBox rtb, string text)
        {
            rtb.SelectionFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            rtb.SelectionColor = Color.FromArgb(51, 65, 85);
            rtb.AppendText(text);
        }
    }
}
