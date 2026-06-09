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
    echo FEHLER: csc.exe nicht gefunden > "%~dp0build.log"
    exit /b 1
)

echo Compiler: !CSC! > "%~dp0build.log"
echo. >> "%~dp0build.log"

"!CSC!" /target:winexe /out:"%~dp0LoginTimer.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0LoginTimer.cs" >> "%~dp0build.log" 2>&1

if !ERRORLEVEL! neq 0 (
    echo. >> "%~dp0build.log"
    echo FEHLER beim Kompilieren. >> "%~dp0build.log"
    exit /b 1
)

echo. >> "%~dp0build.log"
echo Fertig! LoginTimer.exe wurde erstellt. >> "%~dp0build.log"
exit /b 0
