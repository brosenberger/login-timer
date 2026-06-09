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
# 1. Compile EXE
scripts\build.bat

# 2. Build MSI  (requires WiX Toolset v3.x)
scripts\build_msi.bat
```

Check `build.log` if EXE build fails. MSI errors are printed to the console by candle/light.

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
4. Attach `LoginTimer.msi` (and optionally `LoginTimer.exe`)
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
  ├─ build.bat           Compile LoginTimer.exe via built-in csc.exe
  ├─ build_msi.bat       Build LoginTimer.msi via WiX candle + light
  ├─ install.bat         Compile and run the lightweight installer
  └─ restart.bat         kill → build → start  (dev workflow)

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

## Known constraints

- Built with **C# 5** (`csc.exe` from .NET Framework 4.x) — no expression-bodied members, no string interpolation, no numeric literal separators.
- **Single source file** (`LoginTimer.cs`) — keep it that way unless the file grows beyond ~1000 lines.
- The `Bitmap` backing the tray icon (`_iconBitmap`) must stay alive while the icon handle is in use — disposing it invalidates the GDI handle and makes the icon disappear.
- `WS_EX_NOACTIVATE` on the overlay prevents focus steal but also means the widget cannot receive keyboard input — intentional.
