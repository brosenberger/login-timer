@echo off
echo Beende laufende LoginTimer-Instanz...
taskkill /F /IM LoginTimer.exe /T >nul 2>&1
timeout /t 2 /nobreak >nul

echo Kompiliere neu...
call "%~dp0build.bat"

if exist "%~dp0LoginTimer.exe" (
    echo Starte LoginTimer...
    start "" "%~dp0LoginTimer.exe"
) else (
    echo FEHLER: LoginTimer.exe nicht gefunden.
    pause
)
