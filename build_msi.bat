@echo off
setlocal enabledelayedexpansion

echo ================================
echo  LoginTimer MSI Builder
echo ================================
echo.

set "WIX_BIN="
if exist "C:\Program Files (x86)\WiX Toolset v3.14\bin\candle.exe" set "WIX_BIN=C:\Program Files (x86)\WiX Toolset v3.14\bin"
if exist "C:\Program Files\WiX Toolset v3.14\bin\candle.exe"        set "WIX_BIN=C:\Program Files\WiX Toolset v3.14\bin"
if exist "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe"  set "WIX_BIN=C:\Program Files (x86)\WiX Toolset v3.11\bin"
if exist "C:\Program Files\WiX Toolset v3.11\bin\candle.exe"        set "WIX_BIN=C:\Program Files\WiX Toolset v3.11\bin"

if not defined WIX_BIN (
    echo [FEHLER] WiX Toolset nicht gefunden!
    echo Download: https://github.com/wixtoolset/wix3/releases/latest
    pause
    exit /b 1
)
echo WiX: !WIX_BIN!
echo.

set "DIR=%~dp0"

if not exist "!DIR!LoginTimer.exe" (
    echo [FEHLER] LoginTimer.exe fehlt - zuerst build.bat ausfuehren.
    pause
    exit /b 1
)

echo Schritt 1/2: candle.exe
"!WIX_BIN!\candle.exe" "!DIR!LoginTimer.wxs" -ext WixUtilExtension -out "!DIR!LoginTimer.wixobj" -nologo
if !ERRORLEVEL! neq 0 ( echo [FEHLER] candle fehlgeschlagen & pause & exit /b 1 )

echo Schritt 2/2: light.exe
"!WIX_BIN!\light.exe" "!DIR!LoginTimer.wixobj" -ext WixUIExtension -ext WixUtilExtension -out "!DIR!LoginTimer.msi" -nologo -sval
if !ERRORLEVEL! neq 0 ( echo [FEHLER] light fehlgeschlagen & pause & exit /b 1 )

if exist "!DIR!LoginTimer.wixobj" del "!DIR!LoginTimer.wixobj"
if exist "!DIR!LoginTimer.wixpdb" del "!DIR!LoginTimer.wixpdb"

echo.
echo Fertig: !DIR!LoginTimer.msi
echo Weitergabe: nur LoginTimer.msi benoetigt.
pause
