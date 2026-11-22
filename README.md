MeteoBarbucha ČR

Přenositelná desktopová aplikace pro Windows (C# / .NET Framework 4.x) určená k stahování, porovnávání a vizualizaci předpovědních meteorologických modelů pro všech 14 krajů České republiky.

Hlavní funkce
1. Multi-modelová analýza pro všech 14 krajů ČR
10 otevřených modelů zdarma bez API klíče (přes Open-Meteo API):
ALADIN CZ (1 km, ČHMÚ)
ECMWF IFS 0.25°
ICON-EU & ICON-D2 (2.2 km) & ICON-D2 RUC (DWD)
GFS Global, GEM Global, JMA Seamless, UKMO Seamless, ARPEGE Europe
Volitelná podpora Meteologix / Kachelmannwetter API (X-API-Key):
Možnost připojit modely Meteologix ECMWF, Europe Swiss 4x4 a Europe Swiss Nowcast HD.

2. Meteorologické prvky a časová okna
8 meteorologických parametrů s automatickým přepočtem při přepnutí a chytrou mezipamětí:
Celkové srážky (mm)
Sníh / Sněžení (cm)
Teplota ve 2 m — Maximum a Minimum (°C)
Teplota v hladině 850 hPa (°C)
Průměrná oblačnost (%)
Nárazy větru a rychlost větru v 10 m (km/h)
Dva režimy období:
Po dnech (1–7 dní)
Vlastní úsek v hodinách (přesný rozsah Od – Do v hodinách)
Výpočet za kraj ze sítě 220 bodů:
Průměr v kraji, Maximum v kraji nebo Krajské město (1 bod).

3. Přesné mapy ČR, Multi-view a Výběr oblast
Realistické mapy (1×1 px LockBits):
Reálné hranice ČR, všech 14 krajů, jemné podbarvení hranic ORP a pozice krajských měst.
Vykreslování z reálné 220bodové sítě bez umělého rozmazání.
Automatické souhrnné mapy: Průměr všech modelů, Maximum ze všech modelů a Rozptyl modelů (Max − Min).
Multi-view & Detail: Přehled všech modelů na jedné obrazovce s možností rozkliknutí do detailu (zoom kolečkem, posun tažením).
Záložka „Výběr oblast“: Synchronizovaný Multi-view všech modelů najednou s předvolbami oblastí (Šumava, Krkonoše, Beskydy, Jižní Morava…), lupou + / −, klávesovými zkratkami (+, −, 0, šipky) a plovoucím ukazatelem hodnot přímo pod kurzorem myši.

4. Export, automatizace a aktualizace
Ukládání PNG map všech modelů a CSV tabulky (UTF-8 s BOM, oddělovač ; pro český Excel).
Volitelný časovač pro automatické stahování a archivaci každých 1 / 3 / 6 / 12 hodin.
Ukládání uživatelského nastavení do %AppData%\MeteoBarbucha\settings.json.
Vestavěný modul pro automatickou aktualizaci z GitHub Releases nebo vlastního version.json.
Stažení a spuštění (Portable)

Aplikace je plně přenositelná jako jediný soubor MeteoBarbucha.exe (nevyžaduje instalaci ani externí knihovny).

Požadavky: Windows 10 / 11 (s vestavěným .NET Framework 4.x) a připojení k internetu.
Spuštění: Stáhněte MeteoBarbucha.exe ze sekce Releases a spusťte jej.
Sestavení ze zdrojových kódů

Pro přeložení .exe na Windows stačí spustit:

bat
build.bat

Skript využívá standardní kompilátor csc.exe z .NET Framework 4.0+ bez nutnosti instalovat Visual Studio nebo další balíčky.

Zdroje dat a licence
Meteorologická data: Open-Meteo API (včetně otevřených dat ČHMÚ ALADIN, DWD ICON, ECMWF, NOAA GFS aj.).
Mapové podklady krajů a ORP: zjednodušené otevřené geometrie ČR.

Made with Antigravity 2026
