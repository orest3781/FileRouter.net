@echo off
rem Launch FileRouter. With no argument it uses the demo config; pass a path
rem to run against your own:   run.bat C:\FileRouter\config.json
cd /d "%~dp0"

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=%~dp0demo\config.json"

if not exist "%CONFIG%" (
    echo Config not found: %CONFIG%
    echo Run  reset.bat  first to create the demo, or pass your own config path.
    pause
    exit /b 1
)

set "EXE=%~dp0src\FileRouter.App\bin\Debug\net8.0-windows\FileRouter.App.exe"
if not exist "%EXE%" (
    echo Building FileRouter for the first time...
    dotnet build src\FileRouter.App\FileRouter.App.csproj -c Debug -v quiet
    if errorlevel 1 ( echo Build failed. & pause & exit /b 1 )
)

start "" "%EXE%" --config "%CONFIG%"
