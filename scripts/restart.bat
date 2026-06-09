@echo off
set "ROOT=%~dp0.."

echo Beende laufende LoginTimer-Instanz...
taskkill /F /IM LoginTimer.exe /T >nul 2>&1
timeout /t 2 /nobreak >nul

echo Kompiliere neu...
call "%~dp0build.bat"

if exist "!ROOT!\dist\LoginTimer.exe" (
    echo Starte LoginTimer...
    start "" "!ROOT!\dist\LoginTimer.exe"
) else (
    echo FEHLER: dist\LoginTimer.exe nicht gefunden.
    pause
)
