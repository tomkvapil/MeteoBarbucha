# Changelog

All notable changes to **MeteoBarbucha ČR** will be documented in this file.

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
