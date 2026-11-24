# Changelog

All notable changes to **MeteoBarbucha ČR** will be documented in this file.

## [1.4.0] - 2026-11-24
### Added
- **Ensemble Threshold Agreement & Percentiles (`EnsembleStats.cs`)**:
  - Added **Model Agreement map & table column (`Shoda modelů nad prahem %`)** showing the exact share and count of models exceeding the active threshold (supporting both `≥` and frost `≤` thresholds).
  - Added **Median (`P50`)** and **10th / 90th Percentiles (`P10` / `P90`)** across summary maps, Kraje & ORP tables, CSV exports, and as a shaded uncertainty band in `MeteogramDialog.cs`.
- **Sliding Accumulation / Intensity Windows (`1h`, `3h`, `6h`, `12h`, `24h`)**: Instant in-memory calculation of maximum short-duration precipitation/snowfall accumulations and parameter extrema within the selected forecast period.
- **ORP Municipality Table (`OrpCatalog.cs`)**: Dedicated **`Tabulka ORP (206 obcí)`** tab evaluating all 206 Czech administrative municipalities from the smoothed 4-NN IDW spatial field, with live search, `≥ 50 %` agreement filter, sortable numeric columns, CSV export, and double-click meteogram/sounding drill-down.
- **Convective & Winter Parameters**: Added **CAPE (`J/kg`)**, **Freezing Level Height (`0 °C` isotherm, `m`)**, and **2m Dew Point (`°C`)**, plus **ČHMÚ warning threshold presets (`Prahy ČHMÚ ▾`)**.
- **Enhanced Forecast Thermodiagram (`ThermodiagramDialog.cs`)**: Expanded vertical profile to **13 pressure levels (`1000–200 hPa`)** with dry adiabats, explicit proxy-model attribution banner, and real-time convective indices (**LCL, K-Index, Total Totals, Lifted Index, CAPE, Freezing Level**).
- **Stale Model Run Detection**: Automatic orange warning badges (`⚠ >12h`) in table headers, map headers, and logs whenever a model initialization timestamp is older than 12 hours.
- **Settings Dialog (`SettingsDialog.cs`), Security & Reliability**:
  - Moved data source, API keys, output directory, background timer, cache management, and diagnostics into a dedicated **`⚙ Nastavení`** dialog so the main sidebar fits cleanly without scrolling.
  - Added **Windows DPAPI encryption (`ProtectedData`)** for API keys in `settings.json` with automatic migration from legacy plaintext keys.
  - Added **72-hour offline cache fallback** (`OFFLINE ZÁLOHA` banner when offline), **rotating file logger (`AppLogger.cs`)** with one-click **Diagnostics dialog**, determinate download progress with **`✕ Zrušit` cancel button**, **model group presets** (`Hi-Res`, `Globální`, `Alt. zdroje`), **timeline keyboard shortcuts** (`Space`, `←`/`→`), **live data-age indicator**, and **random `+1..12 min` time jitter** on scheduled background downloads.
### Changed
- **Zero-request startup**: No weather models are pre-selected or auto-downloaded on startup; automatic parameter-switch fetching activates only after the user selects models and initiates the first run.
- **Unified forecast window**: Horizon mode now evaluates the exact same future hourly window (`nowHour` to `+N × 24h`) across Open-Meteo, MET Norway, Bright Sky, and Meteologix, eliminating day-1 partial-day discrepancies.
- **Transparent provenance & regional cartography**: Derived parameters from alternative sources are explicitly tagged (`*` / `Pozn.`), and 14-point Meteologix data renders discrete Kraje polygons instead of misleading point interpolation.
- Updated the built-in **User Guide (`HelpDialog.cs`)** for v1.4.0.

## [1.3.3] - 2026-11-24
### Added
- **Multi-source open weather providers (`AlternativeWeatherProviders.cs`)** to eliminate single-provider rate-limit bottlenecks:
  - Integrated **MET Norway (`api.met.no` Locationforecast 2.0 / YR.no ECMWF blend)** and **Bright Sky (`api.brightsky.dev` DWD Open Data / MOSMIX-ICON)** — both free and requiring no API key.
  - Added **`Automaticky (více zdrojů + záloha při 429)`** mode with automatic failover to MET Norway + Bright Sky whenever Open-Meteo returns HTTP `429 Too Many Requests`, plus a dedicated **`Alternativní zdroje (MET Norway + Bright Sky)`** mode making zero Open-Meteo requests.
- **Timeline Play / Pause animation (`▶ Přehrát` / `⏸ Pauza`)** next to the timeline slider to animate hourly/daily forecast steps across the table, Multi-view, Detail view, and synchronized *Výběr oblast* tab.
- **Optional Open-Meteo API key / custom server URL** input (`customer-api.open-meteo.com` or self-hosted Open-Meteo instance) persisted in `SettingsManager.cs`.
- **Clear Cache button (`Vymazat mezipaměť (cache)`)** to purge both in-memory and 12h GZIP disk caches on demand, plus 30-minute disk caching for Open-Meteo `/static/meta.json` model run timestamps.
### Changed
- **~22% Open-Meteo API location reduction** (from 234 to 182 locations) with zero loss of spatial resolution inside Czechia: kept the full `0.25° × 0.35°` grid across Czechia and a `0.26°` cross-border interpolation buffer while skipping distant foreign points.
- Updated `MapRenderer` grid cache keying with coordinate hashing so switching between full and alternative grids recomputes exact 4-NN IDW weights cleanly.
- Updated the built-in **User Guide (`HelpDialog.cs`)** for v1.3.3.
### Fixed
- Fixed **Meteologix / Kachelmannwetter API v02** endpoints in `WeatherService.cs` (`404 Not Found` caused by invalid `/trend/{model}` path):
  - Key verification now checks `/v02/tools/astronomy/{lat}/{lon}` (token validity) and `/v02/forecast/{lat}/{lon}/3day` (location permissions).
  - Forecast fetcher updated to `/v02/forecast/{lat}/{lon}/advanced/1h` and `/3day`, and now skips non-whitelisted regions returning HTTP `403` (`continue` instead of aborting) so Hobby-tier whitelisted locations still load.

## [1.3.2] - 2026-11-24
### Added
- **Data source selector (`Zdroj dat`)** allowing users to choose between `Prefer Meteologix API + Open-Meteo`, `Open-Meteo only`, or `Meteologix / Kachelmann API only` (ideal for shared networks such as ČHMÚ to avoid Open-Meteo 429 rate limits).
- **API Key Verification button (`Ověřit`)** with live status feedback (`✓ valid` / `✗ invalid` HTTP status) for Meteologix `X-API-Key`.
- Active data source label displayed in the status bar after each fetch.
### Changed
- Renamed and updated the background timer to **`Automaticky stahovat každých`** (auto-fetches fresh data into the UI without auto-saving files to disk).
- Updated the built-in **User Guide (`HelpDialog.cs`)** for v1.3.2.

## [1.3.1] - 2026-11-23
### Added
- **Emagram mode toggle** (`Emagram (rovné izotermy)`) in `ThermodiagramDialog.cs` to switch between Skew-T and straight-isotherm Emagram vertical profiles.
- Prominent **red crossed-circle indicator** (`⊘`) on model maps when a model does not provide data for the selected parameter (e.g., `ALADIN CZ 1 km` for 850 hPa temperature).
### Fixed
- Ensured models without data for a given parameter are strictly excluded from multi-model summary calculations (Average, Maximum, Spread).
### Changed
- Updated the built-in **User Guide (`HelpDialog.cs`)** for v1.3.1.

## [1.3.0] - 2026-11-23
### Added
- **Forecast Thermodiagram (Skew-T / Log-P)** (`ThermodiagramDialog.cs`) for the **Significant Weather** parameter:
  - Displays vertical atmospheric profiles of temperature ($T$), dew point ($T_d$), $0\,^\circ\text{C}$ isotherm, and wind speed (`km/h`) across `1000–200 hPa` pressure levels with an interactive time slider.
  - Opens on click only for individual weather models (disabled on multi-model summary maps: Average, Maximum, Spread).
  - Automatically maps `ALADIN CZ 1 km` (surface-only) to `ALADIN Central Europe 2 km` for upper-air pressure levels.
- Updated the built-in **User Guide (`HelpDialog.cs`)** to reflect all features through v1.3.0.

## [1.2.2] - 2026-11-23
### Added
- **Model run initialization time** (`Běh modelu: dd.MM.yyyy HH:mm (XXZ)`) fetched from Open-Meteo `/static/meta.json` and displayed in every map header.
- **12-hour GZIP-compressed disk cache** (`DiskCache.cs`) in `%AppData%\MeteoBarbucha\cache\` with automatic pruning (max 30 files).
- **Smooth RGB 4-neighbor IDW color blending** for **Significant Weather** maps.
### Changed
- Merged the 14 regional points and 220 spatial grid points into a **single combined HTTP batch request** (234 locations) to reduce API rate-limit pressure.
- Added automatic **HTTP 429 retry** respecting `Retry-After` headers with a clear user-facing error message if limits persist.
- Added **500 ms debounce** on parameter selection changes.

## [1.2.1] - 2026-11-23
### Added
- **Timeline slider (`Časová osa`)** above tabs to step through individual forecast hours/days across both tables and maps.
- **Spaghetti Meteogram (`MeteogramDialog.cs`)** on map click showing all selected models' time series for that location.
- **Universal warning threshold (`Výstraha ≥`)** with red diagonal hatch overlay and `⚠` region badges for any parameter.
### Changed
- **10× faster map rendering** via precomputed Czechia mask and **4-nearest-neighbor IDW** lookup tables (`MapRenderer.cs`) for smooth spatial gradients without boxy artifacts.
- Background multi-view rendering via `Task.Run`, `CancellationToken` support for rapid parameter switching, and 6-entry LRU memory cache limit.

## [1.2.0] - 2026-11-23
### Added
- **Significant Weather (WMO weather codes 0–99)** parameter with Czech category labels and custom legend.
- Default GitHub Releases manifest URL (`https://api.github.com/repos/tomkvapil/MeteoBarbucha/releases/latest`) with automatic startup update checks.

## [1.1.0] - 2026-11-23
### Added
- **UTCI (Universal Thermal Climate Index)** calculation (`UtciCalculator.cs`, Bröde et al. 2012) across ALADIN CZ and all Open-Meteo models, with configurable heat-stress warning threshold (`≥ 32 °C`) and hover warning badges.
- Visible **version number and release date** in the bottom status bar.

## [1.0.0] - 2026-11-21
### Added
- Initial portable Windows release (`MeteoBarbucha.exe`) with 10 open models (including `ALADIN CZ 1 km` and `ICON-D2 RUC`), optional Meteologix API support, 220-point spatial grid, real Czechia/Kraje/ORP borders, regional capital markers, Multi-view & synchronized **Výběr oblast** zoomable maps, hover tooltips, CSV export, auto-scheduler, settings persistence, and built-in auto-updater.
