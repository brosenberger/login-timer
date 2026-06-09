@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."

set "CSC="
if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
if not defined CSC (
    if exist "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
        set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
)

if not defined CSC (
    echo FEHLER: csc.exe nicht gefunden.
    pause
    exit /b 1
)

echo Kompiliere Installer...
"!CSC!" /target:winexe /out:"!ROOT!\installer\Installer.exe" ^
    /r:System.Windows.Forms.dll ^
    /r:System.Drawing.dll ^
    "!ROOT!\installer\Installer.cs" > "!ROOT!\install.log" 2>&1

if !ERRORLEVEL! neq 0 (
    echo FEHLER beim Kompilieren. Siehe install.log
    pause
    exit /b 1
)

echo Starte Installer...
start "" "!ROOT!\installer\Installer.exe"
