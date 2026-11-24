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
            Size = new Size(800, 660);
            MinimumSize = new Size(640, 480);
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
            AppendHeading(rtb, "MeteoBarbucha ČR (v" + UpdateService.CurrentVersion + ") — Návod k použití", 15f);
            AppendText(rtb, "Aplikace stahuje předpovědi pro všech 14 krajů ČR i prostorovou síť 220 bodů napříč numerickými modely, počítá statistiky, vykresluje plynulé mapy, meteogramy i vertikální termodiagramy.\n\n");

            AppendHeading(rtb, "1. Rychlý postup (krok za krokem)", 11.5f);
            AppendText(rtb,
                "1) V levém panelu vyberte Parametr předpovědi (Srážky, Sníh, Teplota 2m Max/Min, T850, Oblačnost, Vítr/Nárazy, UTCI nebo Significant Weather).\n" +
                "   • Při přepnutí parametru se data načtou automaticky (s 500ms prodlevou a využitím 12h mezipaměti).\n" +
                "2) Volitelně zapněte Výstraha ≥ a nastavte prahovou hodnotu (červené šrafování + symbol ⚠ na mapě).\n" +
                "3) Zvolte výpočet za kraj (Průměr v kraji, Maximum v kraji nebo Krajské město).\n" +
                "4) Vyberte období — buď Po dnech (1–7 dní), nebo Vlastní úsek v hodinách (Od – Do).\n" +
                "5) Zaškrtněte modely a klikněte na „Stáhnout data a spočítat“.\n" +
                "6) Pro uložení všech PNG map a CSV tabulky klikněte na „Uložit snímky a tabulku“.\n\n");

            AppendHeading(rtb, "2. Časová osa, Meteogram a Předpovědní termodiagram", 11.5f);
            AppendText(rtb,
                "• Časová osa nad záložkami: Umožňuje krokovat po jednotlivých dnech nebo hodinách předpovědi současně pro tabulku i mapy (krajní poloha vlevo = souhrn za celé období).\n" +
                "• Spaghetti meteogram: Kliknutím na libovolné místo v detailu mapy nebo v záložce Výběr oblast se otevře graf vývoje daného prvku v čase pro všechny modely.\n" +
                "• Předpovědní termodiagram (Skew-T / Emagram): Při zvoleném parametru Significant Weather otevře kliknutí na mapu jednotlivého modelu vertikální sondáž atmosféry (teplota T, rosný bod Td a vítr v hladinách 1000–200 hPa) s vlastním posuvníkem času a přepínačem mezi Skew-T a Emagramem. Na souhrnných mapách (Průměr, Max, Rozptyl) se neotvírá.\n" +
                "• Chybějící data modelu: Pokud některý model daný parametr neposkytuje (např. ALADIN CZ pro 850 hPa), zobrazí se na jeho mapě červený přeškrtnutý kruh a do souhrnných map (Průměr, Max, Rozptyl) není zahrnut.\n\n");

            AppendHeading(rtb, "3. Mapy ČR (Multi-view, Detail a Výběr oblast)", 11.5f);
            AppendText(rtb,
                "• Mapy mají reálné hranice ČR, 14 krajů, světle šedé hranice ORP, pozice krajských měst a v hlavičce zobrazují přesný čas běhu modelu.\n" +
                "• Mapy jsou vyhlazené jemnou 4-bodovou IDW interpolací z 220bodové sítě.\n" +
                "• Záložka Výběr oblast: Synchronizuje přiblížení a posun napříč všemi modely najednou (předvolby oblastí, Lupa +/-, klávesy +, −, 0, šipky). Najetím myši se ukáže plovoucí štítek s přesnou hodnotou pod kurzorem.\n\n");

            AppendHeading(rtb, "4. Zdroje dat, ověření klíče a 12h mezipaměť", 11.5f);
            AppendText(rtb,
                "• Zdroj dat: V levém panelu můžete zvolit „Preferovat Meteologix API + Open-Meteo“, „Pouze Open-Meteo API“ nebo „Pouze Meteologix / Kachelmann API“ (vhodné při sdílené síti, např. v ČHMÚ, aby se předešlo limitům Open-Meteo 429).\n" +
                "• Ověření klíče: Vedle pole X-API-Key je tlačítko „Ověřit“, které ihned otestuje platnost vašeho Meteologix API klíče.\n" +
                "• 10 otevřených modelů (Open-Meteo API): ALADIN CZ (1 km, ČHMÚ), ECMWF IFS 0.25°, ICON-EU, ICON-D2, ICON-D2 RUC, GFS, GEM, JMA, UKMO, ARPEGE.\n" +
                "• Meteologix API (X-API-Key): Meteologix ECMWF, Europe Swiss 4x4, Europe Swiss Nowcast HD.\n" +
                "• Automaticky stahovat každých X h: Časovač pouze stahuje čerstvá data do aplikace (bez automatického ukládání na disk).\n\n");

            AppendHeading(rtb, "5. Automatické aktualizace a přenositelnost", 11.5f);
            AppendText(rtb,
                "• Aplikace je přenositelná (jediný soubor MeteoBarbucha.exe). Při každém spuštění automaticky kontroluje aktualizace z GitHub Releases a přes tlačítko Aktualizace je lze spravovat i ručně.");
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
