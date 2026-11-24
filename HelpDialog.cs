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
            Size = new Size(860, 700);
            MinimumSize = new Size(660, 500);
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
            AppendText(rtb, "Aplikace slouží jako multi-modelový analyzátor předpovědí pro meteorology. Vyhodnocuje 14 krajů i všech 206 ORP v ČR napříč otevřenými zdroji (Open-Meteo, MET Norway, Bright Sky DWD) i Meteologix API.\n\n");

            AppendHeading(rtb, "1. Rychlý postup a šetrný start bez automatického stahování", 11.5f);
            AppendText(rtb,
                "1) Po spuštění nejsou záměrně zaškrtnuty žádné modely a nic se nestahuje, aby se šetřily limity otevřených API.\n" +
                "2) Zaškrtněte požadované modely (nebo klikněte na rychlou předvolbu Hi-Res, Globální, Alt. zdroje či Vše) a stiskněte „Stáhnout data a spočítat“.\n" +
                "   • Probíhající stahování ukazuje přesný postup a lze jej kdykoli přerušit tlačítkem „✕ Zrušit“.\n" +
                "   • Jakmile proběhne první stažení, každá další změna parametru již automaticky načte data pro vybrané modely (s 500ms prodlevou a využitím 12h mezipaměti).\n" +
                "3) Všechna období v režimu „Horizont dopředu“ začínají od aktuální celé hodiny (+24 až +168 h), takže se srážky i teploty porovnávají napříč všemi zdroji nad identickým časovým úsekem.\n\n");

            AppendHeading(rtb, "2. Pravděpodobnostní shoda, percentily, klouzavé úhrny a ORP", 11.5f);
            AppendText(rtb,
                "• Mapa a sloupec Shoda modelů (%): Ukazuje procento modelů překračujících zvolený práh (např. ≥ 30 mm u srážek nebo ≤ −12 °C u minimální teploty). Tlačítko „Prahy ČHMÚ ▾“ nabízí přednastavené stupně nebezpečí.\n" +
                "• Medián (P50) a percentily P10 / P90: Robustní statistiky odolné vůči odlehlým běhům jednotlivých modelů v tabulkách, mapách i ve stínovaném pásmu meteogramu.\n" +
                "• Klouzavý úhrn / okno (1h, 3h, 6h, 12h, 24h): Okamžitě bez síťového dotazu přepočítá maximální krátkodobou intenzitu nebo klouzavý úhrn v rámci zvoleného období.\n" +
                "• Záložka Tabulka ORP (206 obcí): Hodnoty pro všech 206 obcí s rozšířenou působností z vyhlazeného pole mapy, s vyhledáváním, filtrem nadprahové shody, řazením podle libovolného sloupce, exportem do CSV a dvojklikem pro otevření meteogramu.\n" +
                "• Kontrola stáří běhu a odvozených veličin: Modely se stářím běhu > 12 hodin jsou zvýrazněny oranžovým varováním (⚠ >12h). Dopočtené veličiny u alternativních zdrojů jsou označeny symbolem * a poznámkou v hlavičce mapy.\n\n");

            AppendHeading(rtb, "3. Konvekční parametry, Termodiagram a Časová osa", 11.5f);
            AppendText(rtb,
                "• 13 parametrů: Srážky, Sníh, T2m Max/Min, T850, Rosný bod 2m, Oblačnost, Nárazy a Rychlost větru 10m, CAPE (J/kg), Výška nulové izotermy (m n. m.), UTCI a Significant Weather.\n" +
                "• Rozšířený předpovědní termodiagram (Skew-T / Emagram): U parametru Significant Weather otevře kliknutí na mapu jednotlivého modelu vertikální sondáž ve 13 hladinách (1000–200 hPa) se suchými adiabatami a panelem indexů (LCL, K-Index, Total Totals, Lifted Index, CAPE, hladina 0 °C). Pokud přízemní zdroj nemá výškové hladiny, diagram transparentně uvede použitý výškový model.\n" +
                "• Klávesové zkratky časové osy: Mezerník spustí/pozastaví animaci (▶ Přehrát / ⏸ Pauza), šipky ← / → (v záložce Výběr oblast Alt+← / Alt+→) krokují po hodinách.\n\n");

            AppendHeading(rtb, "4. Okno ⚙ Nastavení, šifrování klíčů, offline záloha a diagnostika", 11.5f);
            AppendText(rtb,
                "• Tlačítko „⚙ Nastavení“ v horní liště sdružuje výběr zdroje dat, API klíče, výstupní složku, automatické stahování a správu mezipaměti, takže levý panel zůstává přehledný.\n" +
                "• Zabezpečení DPAPI: API klíče (Meteologix i Open-Meteo) se do settings.json ukládají šifrovaně pomocí vestavěné ochrany Windows DPAPI vázané na váš účet.\n" +
                "• Rozložené automatické stahování: Časovač automaticky přidává náhodný posun (+1 až +12 min), aby se požadavky více stanic nesešly ve stejný okamžik.\n" +
                "• Offline záloha a Diagnostika: Při výpadku sítě aplikace automaticky načte poslední dostupná data z mezipaměti (až 72 h) s výrazným označením OFFLINE ZÁLOHA. Tlačítko Diagnostika vypíše kompletní provozní přehled a otevře složku rotovaných logů.");
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
