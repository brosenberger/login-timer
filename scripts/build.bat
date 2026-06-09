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
    echo FEHLER: csc.exe nicht gefunden > "!ROOT!\build.log"
    exit /b 1
)

echo Compiler: !CSC! > "!ROOT!\build.log"
echo. >> "!ROOT!\build.log"

"!CSC!" /target:winexe /out:"!ROOT!\dist\LoginTimer.exe" ^
    /r:System.Windows.Forms.dll ^
    /r:System.Drawing.dll ^
    "!ROOT!\src\LoginTimer.cs" >> "!ROOT!\build.log" 2>&1

if !ERRORLEVEL! neq 0 (
    echo. >> "!ROOT!\build.log"
    echo FEHLER beim Kompilieren. >> "!ROOT!\build.log"
    exit /b 1
)

echo. >> "!ROOT!\build.log"
echo Fertig dist\LoginTimer.exe wurde erstellt. >> "!ROOT!\build.log"
exit /b 0
