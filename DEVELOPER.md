# Developer Notes

## How to release a new version

### 1 — Code changes

Edit `src/LoginTimer.cs`, test with `scripts\restart.bat` (kill → rebuild → restart).

### 2 — Bump the version

Two places must stay in sync:

| File | Field | Example |
|------|-------|---------|
| `LoginTimer.wxs` | `Version="1.0.0.0"` | → `Version="1.1.0.0"` |
| `CHANGELOG.md` | new `## [1.1.0] — YYYY-MM-DD` section | add entries |

WiX version format is always **four parts** (`major.minor.patch.build`).  
`MajorUpgrade` in the WXS ensures the old version is removed automatically on install.

### 3 — Build

```bat
build-all.bat
```

Builds all three artifacts in sequence into `dist\`:

```
dist\LoginTimer.exe    ← the application
dist\LoginTimer.msi    ← standard installer (requires WiX Toolset v3.x)
dist\Installer.exe     ← lightweight fallback installer
```

`build-all.bat` sets `NOPAUSE=1` internally so sub-scripts do not block
with interactive pauses.  Check `build.log` (EXE) or `install.log` (Installer.exe)
on failure.  MSI errors are printed to the console by candle/light.

### 4 — Commit & tag

```powershell
cd C:\Users\brosenberger\login_timer
git add -A
git commit -m "Release v1.1.0: <short summary>"
git tag v1.1.0
git push origin main --tags
```

### 5 — GitHub Release

1. Go to the repo → **Releases** → **Draft a new release**
2. Select tag `v1.1.0`
3. Copy the relevant section from `CHANGELOG.md` as description
4. Attach the following artifacts from `dist\`:

   | File | Include when |
   |---|---|
   | `LoginTimer.msi` | Always — standard install path |
   | `LoginTimer.exe` | Always — portable / manual use |
   | `Installer.exe` | Always — fallback for restricted machines |

5. **Publish release**

---

## Project structure

```
src/
  └─ LoginTimer.cs       Main application (single file)
       ├─ Program            Entry point, mutex, error logging
       ├─ POINT/RECT/        Win32 structs for P/Invoke
       │   WINDOWPLACEMENT
       ├─ WindowHelper       Per-monitor active-window detection (Z-order walk)
       ├─ AppTracker         Per-app time storage (apps.csv)
       ├─ DayData            Login-time CSV storage (data.csv)
       ├─ TrayApp            ApplicationContext: tray icon, timers, session events
       ├─ OverlayForm        Floating always-on-top widget
       ├─ AnchorForm         Hidden window for Restart Manager / installer detection
       └─ HistoryForm        Statistics window (tabs: days / weeks / months / apps)

installer/
  ├─ LoginTimer.wxs      WiX MSI installer definition
  ├─ license.rtf         License text displayed in the MSI wizard
  └─ Installer.cs        Lightweight standalone installer (no WiX required)

scripts/
  ├─ build.bat             Compile LoginTimer.exe via built-in csc.exe
  ├─ build_msi.bat         Build LoginTimer.msi via WiX candle + light
  ├─ build-installer.bat   Compile Installer.exe only (no launch)
  ├─ install.bat           Compile Installer.exe then run it immediately
  └─ restart.bat           kill → build → start  (dev workflow)

build-all.bat              Root-level script: builds all three dist/ artifacts in order

docs/screenshots/        PNG screenshots used in README.md
```

---

## Data files (runtime, not in repo)

| Path | Content |
|------|---------|
| `%AppData%\LoginTimer\data.csv` | `yyyy-MM-dd,seconds` — one line per day |
| `%AppData%\LoginTimer\apps.csv` | `yyyy-MM-dd,processname,seconds` — one line per day+app |
| `%AppData%\LoginTimer\overlay.pos` | `x,y` — last widget position |
| `%AppData%\LoginTimer\error.log` | Appended on unhandled exceptions |

---

## Deployment / distribution

### Which file goes where

| Scenario | Ship | Notes |
|---|---|---|
| Normal corporate or home PC | `LoginTimer.msi` | Standard wizard, adds to Programs and Features, clean uninstall |
| PC with `DisableMSI` Group Policy | `LoginTimer.exe` + `Installer.exe` | Run `Installer.exe` — plain EXE, no Windows Installer involved |
| Portable / no install wanted | `LoginTimer.exe` alone | Drop in any folder, create autostart shortcut manually |

### How to detect the Group Policy block

The error dialog reads:

> *"The system administrator has set policies to prevent this installation."*

This is the Windows Installer `DisableMSI` policy (`HKLM\SOFTWARE\Policies\Microsoft\Windows\Installer`, value `DisableMSI`).
Check with: `reg query "HKLM\SOFTWARE\Policies\Microsoft\Windows\Installer" /v DisableMSI`  
A value of `1` (All) or `2` (Non-managed apps) blocks MSI installs for normal users.

### What `Installer.exe` does

Identical end-state to the MSI, implemented as a plain .NET EXE so no Windows
Installer is invoked:

1. Creates `%LocalAppData%\LoginTimer\` if missing
2. Copies `LoginTimer.exe` next to itself into that folder
3. Creates `%AppData%\Microsoft\Windows\Start Menu\Programs\Startup\LoginTimer.lnk`
4. Shows a success dialog and optionally launches the app

No admin rights required. No registry-based uninstall entry (remove manually
by deleting the folder and shortcut if desired).

---

## Known constraints

- Built with **C# 5** (`csc.exe` from .NET Framework 4.x) — no expression-bodied members, no string interpolation, no numeric literal separators.
- **Single source file** (`LoginTimer.cs`) — keep it that way unless the file grows beyond ~1000 lines.
- The `Bitmap` backing the tray icon (`_iconBitmap`) must stay alive while the icon handle is in use — disposing it invalidates the GDI handle and makes the icon disappear.
- `WS_EX_NOACTIVATE` on the overlay prevents focus steal but also means the widget cannot receive keyboard input — intentional.
