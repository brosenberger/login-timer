@echo off
setlocal enabledelayedexpansion

echo ================================
echo  LoginTimer MSI Builder
echo ================================
echo.

set "ROOT=%~dp0.."

set "WIX_BIN="
if exist "C:\Program Files (x86)\WiX Toolset v3.14\bin\candle.exe" set "WIX_BIN=C:\Program Files (x86)\WiX Toolset v3.14\bin"
if exist "C:\Program Files\WiX Toolset v3.14\bin\candle.exe"        set "WIX_BIN=C:\Program Files\WiX Toolset v3.14\bin"
if exist "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe"  set "WIX_BIN=C:\Program Files (x86)\WiX Toolset v3.11\bin"
if exist "C:\Program Files\WiX Toolset v3.11\bin\candle.exe"        set "WIX_BIN=C:\Program Files\WiX Toolset v3.11\bin"

if not defined WIX_BIN (
    echo [FEHLER] WiX Toolset nicht gefunden!
    echo Download: https://github.com/wixtoolset/wix3/releases/latest
    if not defined NOPAUSE pause
    exit /b 1
)
echo WiX: !WIX_BIN!
echo.

if not exist "!ROOT!\dist\LoginTimer.exe" (
    echo [FEHLER] dist\LoginTimer.exe fehlt - zuerst scripts\build.bat ausfuehren.
    if not defined NOPAUSE pause
    exit /b 1
)

rem Cleanup stale output before build
if exist "!ROOT!\dist\LoginTimer.msi" del /f "!ROOT!\dist\LoginTimer.msi"

rem Run candle + light from the installer directory so relative paths in the
rem WXS (Source="..\dist\LoginTimer.exe", license.rtf) resolve correctly.
pushd "!ROOT!\installer"

echo Schritt 1/2: candle.exe
"!WIX_BIN!\candle.exe" "LoginTimer.wxs" -ext WixUtilExtension -out "LoginTimer.wixobj" -nologo
if !ERRORLEVEL! neq 0 (
    popd
    echo [FEHLER] candle fehlgeschlagen
    if not defined NOPAUSE pause
    exit /b 1
)

echo Schritt 2/2: light.exe
"!WIX_BIN!\light.exe" "LoginTimer.wixobj" -ext WixUIExtension -ext WixUtilExtension -out "..\dist\LoginTimer.msi" -nologo -sval
if !ERRORLEVEL! neq 0 (
    popd
    echo [FEHLER] light fehlgeschlagen
    if not defined NOPAUSE pause
    exit /b 1
)

if exist "LoginTimer.wixobj" del "LoginTimer.wixobj"
if exist "LoginTimer.wixpdb" del "LoginTimer.wixpdb"

popd

echo.
echo Fertig: dist\LoginTimer.msi
if not defined NOPAUSE (
    echo Weitergabe: nur LoginTimer.msi benoetigt.
    pause
)
exit /b 0
