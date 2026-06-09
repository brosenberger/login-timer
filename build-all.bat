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

echo Bereinige dist\ ...
if exist "%~dp0dist\LoginTimer.exe"    del /f "%~dp0dist\LoginTimer.exe"
if exist "%~dp0dist\LoginTimer.msi"    del /f "%~dp0dist\LoginTimer.msi"
if exist "%~dp0dist\LoginTimer.wixpdb" del /f "%~dp0dist\LoginTimer.wixpdb"
if exist "%~dp0dist\Installer.exe"     del /f "%~dp0dist\Installer.exe"
if exist "%~dp0dist\LoginTimer.zip"    del /f "%~dp0dist\LoginTimer.zip"
echo.

set "NOPAUSE=1"

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

echo [4/4] Erstelle LoginTimer.zip ...
set "LT_F1=%~dp0dist\LoginTimer.exe"
set "LT_F2=%~dp0dist\LoginTimer.msi"
set "LT_F3=%~dp0dist\Installer.exe"
set "LT_OUT=%~dp0dist\LoginTimer.zip"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Force -Path $env:LT_F1,$env:LT_F2,$env:LT_F3 -DestinationPath $env:LT_OUT"
if !ERRORLEVEL! neq 0 (
    echo.
    echo WARNUNG: ZIP konnte nicht erstellt werden ^(PowerShell 5+ erforderlich^).
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
