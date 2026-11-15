@echo off
setlocal enabledelayedexpansion
set "ROOT=%~dp0.."

echo Beende laufende LoginTimer-Instanz...
rem Without /F taskkill sends WM_CLOSE, so LoginTimer saves its data first.
taskkill /IM LoginTimer.exe >nul 2>&1
timeout /t 3 /nobreak >nul
taskkill /F /IM LoginTimer.exe /T >nul 2>&1
timeout /t 1 /nobreak >nul

echo Kompiliere neu...
call "%~dp0build.bat"
if !ERRORLEVEL! neq 0 (
    echo FEHLER beim Kompilieren - siehe build.log
    pause
    exit /b 1
)

if exist "!ROOT!\dist\LoginTimer.exe" (
    echo Starte LoginTimer...
    start "" "!ROOT!\dist\LoginTimer.exe"
) else (
    echo FEHLER: dist\LoginTimer.exe nicht gefunden.
    pause
)
