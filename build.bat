@echo off
set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
%CSC% /nologo /target:winexe /out:MeteoBarbucha.exe /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll Program.cs MainForm.cs Models.cs Regions.cs CzechGeoData.cs WeatherService.cs AlternativeWeatherProviders.cs MapRenderer.cs CsvExporter.cs ZoomablePictureBox.cs HelpDialog.cs SettingsManager.cs UpdateService.cs UpdateDialog.cs UtciCalculator.cs MeteogramDialog.cs DiskCache.cs ThermodiagramDialog.cs
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)
echo Build succeeded: MeteoBarbucha.exe
