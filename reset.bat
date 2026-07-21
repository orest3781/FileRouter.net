@echo off
rem Regenerate the demo: 5 sample faxes in demo\inbox, empty route/set-aside
rem folders, and demo\config.json. Real documents are never touched.
cd /d "%~dp0"
dotnet run --project tools\FileRouter.Smoke -- reset-demo
if errorlevel 1 pause
