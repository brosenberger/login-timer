@echo off
setlocal enabledelayedexpansion

echo ============================================
echo  LoginTimer - Build All Artifacts
echo ============================================
echo.
echo Ausgabe: dist\LoginTimer.exe
echo         dist\LoginTimer.msi
echo         dist\Installer.exe
echo.

rem ── Cleanup ─────────────────────────────────────────────────────────────
echo Bereinige dist\ ...
if exist "%~dp0dist\LoginTimer.exe" del /f "%~dp0dist\LoginTimer.exe"
if exist "%~dp0dist\LoginTimer.msi" del /f "%~dp0dist\LoginTimer.msi"
if exist "%~dp0dist\Installer.exe"  del /f "%~dp0dist\Installer.exe"
if exist "%~dp0dist\LoginTimer.zip" del /f "%~dp0dist\LoginTimer.zip"
echo.

rem Suppress interactive pauses in sub-scripts
set "NOPAUSE=1"

rem ── 1/4  LoginTimer.exe ─────────────────────────────────────────────────
echo [1/4] Kompiliere LoginTimer.exe ...
call "%~dp0scripts\build.bat"
if !ERRORLEVEL! neq 0 (
    echo.
    echo BUILD FEHLGESCHLAGEN bei Schritt 1 - Details in build.log
    set "NOPAUSE="
    pause
    exit /b 1
)
echo       OK  dist\LoginTimer.exe
echo.

rem ── 2/4  LoginTimer.msi ─────────────────────────────────────────────────
echo [2/4] Baue LoginTimer.msi ...
call "%~dp0scripts\build_msi.bat"
if !ERRORLEVEL! neq 0 (
    echo.
    echo BUILD FEHLGESCHLAGEN bei Schritt 2
    set "NOPAUSE="
    pause
    exit /b 1
)
echo       OK  dist\LoginTimer.msi
echo.

rem ── 3/4  Installer.exe ──────────────────────────────────────────────────
echo [3/4] Kompiliere Installer.exe ...
call "%~dp0scripts\build-installer.bat"
if !ERRORLEVEL! neq 0 (
    echo.
    echo BUILD FEHLGESCHLAGEN bei Schritt 3 - Details in install.log
    set "NOPAUSE="
    pause
    exit /b 1
)
echo       OK  dist\Installer.exe
echo.

set "NOPAUSE="

rem ── 4/4  ZIP  ────────────────────────────────────────────────────────────
echo [4/4] Erstelle LoginTimer.zip ...
powershell -NoProfile -Command ^
  "Compress-Archive -Force ^
   -Path '%~dp0dist\LoginTimer.exe','%~dp0dist\LoginTimer.msi','%~dp0dist\Installer.exe' ^
   -DestinationPath '%~dp0dist\LoginTimer.zip'"
if !ERRORLEVEL! neq 0 (
    echo.
    echo WARNUNG: ZIP konnte nicht erstellt werden (PowerShell 5+ erforderlich).
    echo          Artefakte in dist\ sind trotzdem vollstaendig.
) else (
    echo       OK  dist\LoginTimer.zip
)
echo.

echo ============================================
echo  Alle Artefakte erfolgreich erstellt:
echo    dist\LoginTimer.exe    (Anwendung)
echo    dist\LoginTimer.msi    (Standard-Installer)
echo    dist\Installer.exe     (Fallback - kein MSI noetig)
echo    dist\LoginTimer.zip    (Alle drei gebuendelt)
echo ============================================
echo.
echo Weitergabe:
echo   Normal:             LoginTimer.msi
echo   Gruppenrichtlinien: LoginTimer.exe + Installer.exe
echo   Alles auf einmal:   LoginTimer.zip
echo.
pause
