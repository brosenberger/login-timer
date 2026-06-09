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
    pause
    exit /b 1
)
echo WiX: !WIX_BIN!
echo.

if not exist "!ROOT!\LoginTimer.exe" (
    echo [FEHLER] LoginTimer.exe fehlt - zuerst scripts\build.bat ausfuehren.
    pause
    exit /b 1
)

rem Run candle + light from the installer directory so relative paths in the
rem WXS (Source="..\LoginTimer.exe", license.rtf) resolve correctly.
pushd "!ROOT!\installer"

echo Schritt 1/2: candle.exe
"!WIX_BIN!\candle.exe" "LoginTimer.wxs" -ext WixUtilExtension -out "LoginTimer.wixobj" -nologo
if !ERRORLEVEL! neq 0 ( popd & echo [FEHLER] candle fehlgeschlagen & pause & exit /b 1 )

echo Schritt 2/2: light.exe
"!WIX_BIN!\light.exe" "LoginTimer.wixobj" -ext WixUIExtension -ext WixUtilExtension -out "..\LoginTimer.msi" -nologo -sval
if !ERRORLEVEL! neq 0 ( popd & echo [FEHLER] light fehlgeschlagen & pause & exit /b 1 )

if exist "LoginTimer.wixobj" del "LoginTimer.wixobj"
if exist "LoginTimer.wixpdb" del "LoginTimer.wixpdb"

popd

echo.
echo Fertig: !ROOT!\LoginTimer.msi
echo Weitergabe: nur LoginTimer.msi benoetigt.
pause
