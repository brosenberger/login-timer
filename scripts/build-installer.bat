@echo off
setlocal enabledelayedexpansion

rem Kompiliert nur Installer.exe -> dist\ (startet NICHT).
rem Wird von scripts\install.bat und build-all.bat aufgerufen.

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
    exit /b 1
)

rem Cleanup stale output before build
if exist "!ROOT!\dist\Installer.exe" del /f "!ROOT!\dist\Installer.exe"

"!CSC!" /target:winexe /out:"!ROOT!\dist\Installer.exe" ^
    /r:System.Windows.Forms.dll ^
    /r:System.Drawing.dll ^
    "!ROOT!\installer\Installer.cs" > "!ROOT!\install.log" 2>&1

if !ERRORLEVEL! neq 0 (
    echo FEHLER beim Kompilieren von Installer.exe - siehe install.log
    exit /b 1
)

exit /b 0
