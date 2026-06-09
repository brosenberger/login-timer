@echo off
setlocal enabledelayedexpansion

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
"!CSC!" /target:winexe /out:"%~dp0Installer.exe" ^
    /r:System.Windows.Forms.dll ^
    /r:System.Drawing.dll ^
    "%~dp0Installer.cs" > "%~dp0install.log" 2>&1

if !ERRORLEVEL! neq 0 (
    echo FEHLER beim Kompilieren. Siehe install.log
    pause
    exit /b 1
)

echo Starte Installer...
start "" "%~dp0Installer.exe"
