# LoginTimer

Windows system tray app that tracks your logged-in time per day.

## Features

- Tracks active login time per day (pauses on Windows lock)
- Floating always-on-top widget showing today's time (HH:MM)
- System tray icon with current time
- History window with statistics:
  - Last 60 days with daily totals
  - Last 16 weeks with weekly averages
  - Last 12 months with monthly averages
  - % change vs. previous day / week / month
- Data stored in `%AppData%\LoginTimer\data.csv`
- Widget position saved across restarts

## Requirements

No installation required — uses only what ships with Windows:
- .NET Framework 4.x (pre-installed on Windows 10/11)
- C# 5 compiler (`csc.exe`, built into Windows)

## Build

```bat
build.bat
```

Output: `LoginTimer.exe`

## MSI Installer

Requires [WiX Toolset v3.x](https://github.com/wixtoolset/wix3/releases/latest) (one-time install).

```bat
build_msi.bat
```

Output: `LoginTimer.msi` — single file, no prerequisites, works on any Windows 10/11 machine.

The MSI installs to `%LocalAppData%\LoginTimer\` (no admin required) and creates an autostart shortcut.

## Usage

- **Tray icon**: shows current day's logged time; double-click opens history
- **Widget**: floating HH:MM display; drag to reposition; double-click opens history
- **Right-click tray icon**: toggle widget visibility, open history, quit

## Files

| File | Description |
|------|-------------|
| `LoginTimer.cs` | Main application source |
| `build.bat` | Compiles `LoginTimer.exe` using built-in `csc.exe` |
| `restart.bat` | Kills running instance, rebuilds, restarts |
| `LoginTimer.wxs` | WiX installer definition |
| `license.rtf` | License text shown in MSI installer |
| `build_msi.bat` | Builds `LoginTimer.msi` (requires WiX) |
| `Installer.cs` | Alternative lightweight installer (no WiX needed) |
| `install.bat` | Compiles and runs the lightweight installer |
