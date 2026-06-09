# LoginTimer

Windows system tray app that tracks your logged-in time per day — automatically pauses when the screen is locked.

## Screenshots

### Widget & Tray icon
<p float="left">
  <img src="docs/screenshots/widget.png" alt="Floating widget" height="60"/>
  &nbsp;&nbsp;
  <img src="docs/screenshots/tray.png" alt="Tray icon" height="60"/>
</p>

The always-on-top widget shows today's time at a glance. The tray icon mirrors the same value and shows a tooltip on hover.

### History — Days
![History Days](docs/screenshots/history-days.png)

Last 60 days with date, weekday, duration, decimal hours, and % change vs. the previous entry.

### History — Weeks
![History Weeks](docs/screenshots/history-weeks.png)

Last 16 weeks grouped by calendar week — total time, number of active days, average per day, and % change vs. previous week.

### History — Months
![History Months](docs/screenshots/history-months.png)

Last 12 months — total, days tracked, average per day, average per week, and % change vs. previous month.

---

## Features

- **Auto-tracking** — starts on login, pauses on Windows lock / remote disconnect, resumes on unlock
- **Floating widget** — always-on-top HH:MM display, draggable, position persisted across restarts
- **Tray icon** — live time rendered directly into the icon; right-click menu to toggle widget or quit
- **History window** — days / weeks / months tabs with averages and ±% change indicators
- **Double-click** widget or tray icon to open history
- **Single-file data store** — `%AppData%\LoginTimer\data.csv` (plain CSV, easy to inspect or back up)
- **No installation required** — uses only the .NET Framework already built into Windows 10/11

---

## Requirements

- Windows 10 or 11
- .NET Framework 4.x (pre-installed on all supported Windows versions)
- No admin rights needed

---

## Install

### Option A — MSI (recommended)

Download `LoginTimer.msi` from the [latest release](../../releases/latest), double-click, follow the wizard.

- Installs to `%LocalAppData%\LoginTimer\`
- Creates an autostart shortcut (runs on every login)
- Optional desktop shortcut (off by default in the wizard)
- Registers in *Programs and Features* for clean uninstall

### Option B — Portable

Download `LoginTimer.exe`, place it anywhere, run it. No installer needed.  
To autostart: drop a shortcut into `shell:startup`.

---

## Build from source

No Visual Studio required — the build uses the C# compiler that ships with Windows.

```bat
build.bat
```

Output: `LoginTimer.exe`

### Build the MSI installer

Requires [WiX Toolset v3.x](https://github.com/wixtoolset/wix3/releases/latest) (one-time install, ~30 MB).

```bat
build_msi.bat
```

Output: `LoginTimer.msi` — self-contained, no prerequisites on the target machine.

---

## Usage

| Action | Result |
|--------|--------|
| Double-click widget or tray icon | Open history window |
| Right-click tray icon | Context menu (toggle widget, history, quit) |
| Drag widget | Reposition — saved automatically |
| Lock Windows (`Win+L`) | Timer pauses |
| Unlock | Timer resumes |

---

## Data

Logged time is stored in plain CSV:

```
%AppData%\LoginTimer\data.csv
```

Format: `yyyy-MM-dd,seconds` — one line per day. Safe to copy or delete individual lines.

---

## License

Freeware — free to use and distribute. No warranty.
