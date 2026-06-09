@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."

echo Kompiliere Installer...
call "%~dp0build-installer.bat"
if !ERRORLEVEL! neq 0 (
    pause
    exit /b 1
)

echo Starte Installer...
start "" "!ROOT!\dist\Installer.exe"
