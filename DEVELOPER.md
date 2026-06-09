# Developer Notes

## How to release a new version

### 1 — Code changes

Edit `LoginTimer.cs`, test with `restart.bat` (kill → rebuild → restart).

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
build.bat

# 2. Build MSI  (requires WiX Toolset v3.x)
build_msi.bat
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
LoginTimer.cs          Main application (single file)
  ├─ Program            Entry point, mutex, error logging
  ├─ DayData            CSV storage (load / save / get / set)
  ├─ TrayApp            ApplicationContext: tray icon, timer, session events
  ├─ OverlayForm        Floating always-on-top widget
  └─ HistoryForm        Statistics window (tabs: days / weeks / months)

build.bat              Compiles LoginTimer.exe via built-in csc.exe
restart.bat            kill → build → start (dev workflow)
build_msi.bat          Builds LoginTimer.msi via WiX candle + light
LoginTimer.wxs         WiX installer definition
license.rtf            License text displayed in the MSI wizard
Installer.cs           Lightweight standalone installer (no WiX)
install.bat            Compiles and runs Installer.exe

docs/screenshots/      PNG screenshots used in README.md
```

---

## Data files (runtime, not in repo)

| Path | Content |
|------|---------|
| `%AppData%\LoginTimer\data.csv` | `yyyy-MM-dd,seconds` — one line per day |
| `%AppData%\LoginTimer\overlay.pos` | `x,y` — last widget position |
| `%AppData%\LoginTimer\error.log` | Appended on unhandled exceptions |

---

## Known constraints

- Built with **C# 5** (`csc.exe` from .NET Framework 4.x) — no expression-bodied members, no string interpolation, no numeric literal separators.
- **Single source file** (`LoginTimer.cs`) — keep it that way unless the file grows beyond ~800 lines.
- The `Bitmap` backing the tray icon (`_iconBitmap`) must stay alive while the icon handle is in use — disposing it invalidates the GDI handle and makes the icon disappear.
- `WS_EX_NOACTIVATE` on the overlay prevents focus steal but also means the widget cannot receive keyboard input — intentional.
