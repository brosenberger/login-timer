# AGENTS.md — LoginTimer

Context document for AI agents. Read this before making any changes.

---

## What this project is

A Windows system tray app written in **C# 5 / .NET Framework 4.x** (single source file,
no project file, no NuGet). It tracks logged-in time per day, pauses on screen lock,
shows a floating overlay widget, and records per-application active time per monitor.

Current version: **1.1.1**

---

## Repo layout

```
src/
  LoginTimer.cs          Single-file application (~1 200 lines)

installer/
  LoginTimer.wxs         WiX 3.x MSI definition
  Installer.cs           Lightweight fallback installer (plain .NET EXE)
  license.rtf            License shown in MSI wizard

scripts/
  build.bat              Compile LoginTimer.exe  →  dist\
  build_msi.bat          Build LoginTimer.msi    →  dist\  (needs WiX 3.x)
  build-installer.bat    Compile Installer.exe   →  dist\  (no launch)
  install.bat            Compile + immediately run Installer.exe
  restart.bat            kill → rebuild → relaunch  (dev loop)

build-all.bat            Root script: runs all four steps, zips result

dist/                    Gitignored (except .gitkeep); all build output lands here
  LoginTimer.exe
  LoginTimer.msi
  Installer.exe
  LoginTimer.zip         All three bundled, produced by build-all.bat step 4/4

docs/screenshots/        PNGs used in README.md
```

---

## Build

### Everything at once
```bat
build-all.bat
```
Cleans `dist\`, compiles EXE, builds MSI, compiles Installer.exe, zips all three.
Sets `NOPAUSE=1` so sub-scripts never block on interactive pauses.
Requires **WiX Toolset v3.x** for the MSI step only.

### Individual scripts
```bat
scripts\build.bat              # LoginTimer.exe
scripts\build_msi.bat          # LoginTimer.msi  (LoginTimer.exe must exist first)
scripts\build-installer.bat    # Installer.exe   (compile only)
scripts\install.bat            # compile + run Installer.exe
```

### Compiler
`csc.exe` from `.NET Framework 4.x` — located automatically at:
- `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (preferred)
- `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe` (fallback)

No Visual Studio, no SDK, no NuGet required.

### Logs
| Log file | Written by |
|---|---|
| `build.log` | `scripts\build.bat` |
| `install.log` | `scripts\build-installer.bat` |

---

## Language constraints (enforced — do not break)

This is **C# 5**. The following modern features are unavailable and must not be used:

| Forbidden | Use instead |
|---|---|
| Expression-bodied members `=> …` | Full `{ return …; }` body |
| String interpolation `$"…{x}…"` | `string.Format("…{0}…", x)` |
| `?.` null-conditional | Explicit null check |
| `??=` null-coalescing assignment | Explicit `if (x == null) x = …` |
| `async` / `await` | `ThreadPool.QueueUserWorkItem` + `SynchronizationContext.Post` |
| `readonly` constructor parameters / property promotion | Classic typed field + body assignment |
| Numeric literal separators `1_000` | `1000` |
| `nameof(…)` | String literal |

All `@param` / `@return` in interface PHPDoc → not applicable (C# project).

---

## Architecture — key classes in `src/LoginTimer.cs`

| Class | Role |
|---|---|
| `Program` | Entry point, named mutex (single instance), unhandled-exception logger |
| `WindowHelper` | Static. Z-order walk via `GetTopWindow`/`GetWindow`; returns topmost visible non-minimised non-system window per monitor. PID cached per tick. |
| `AppTracker` | In-memory `Dictionary<DateTime, Dictionary<string, double>>`. `RecordTick()`, `Save()`, `Load()`. CSV: `%AppData%\LoginTimer\apps.csv` |
| `DayData` | Login-time storage. CSV: `%AppData%\LoginTimer\data.csv` (`yyyy-MM-dd,seconds`) |
| `UpdateChecker` | Static. Queries GitHub `releases/latest` API on a ThreadPool thread, regex-parses `tag_name` + `LoginTimer.exe` asset URL/sha256 digest, compares against `Program.Version`. Forces TLS 1.2 (`SecurityProtocolType` 3072). Self-update: download next to EXE, verify digest, rename running EXE → `.old`, move new in place, relaunch with `--updated` (Main then waits up to 15 s for the mutex); `.old` cleanup retries on next start. |
| `TrayApp` | `ApplicationContext`. Owns tray icon, 30 s timer, 10 s app timer, `OverlayForm`, `AnchorForm`. |
| `OverlayForm` | Floating widget. `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW`. `SetWindowPos(HWND_TOPMOST)` re-asserted every 1 s. Position saved on `LocationChanged`. |
| `AnchorForm` | Hidden 1×1 window, `Text="LoginTimer"`, no `WS_EX_TOOLWINDOW`. Exists solely so Restart Manager and `util:CloseApplication` can identify the running process. |
| `HistoryForm` | Statistics window. Four tabs: Tage / Wochen / Monate / Apps. `TopMost = true`. |

### Threading
- `OnAppTick` dispatches `WindowHelper.GetActiveAppsPerMonitor()` to a `ThreadPool` thread.
- Results are marshalled back to the UI thread via `SynchronizationContext.Post`.
- `SynchronizationContext.Current` is `null` in the `TrayApp` constructor (before `Application.Run`); it is captured **lazily on the first tick**.

### Clean shutdown
- `TrayApp._exiting` bool guards against re-entrant `Application.Exit()` calls.
- `OnExit` calls `AllowClose()` on both `_overlay` and `_anchor` before disposing, so `OnFormClosing` lets the close proceed rather than calling `Application.Exit()` again.
- `AnchorForm.OnFormClosing`: external `WM_CLOSE` (from installer) → `Application.Exit()`. `_allowClose` set → let close.

---

## Data files (runtime, not in repo)

| Path | Format |
|---|---|
| `%AppData%\LoginTimer\data.csv` | `yyyy-MM-dd,seconds` |
| `%AppData%\LoginTimer\apps.csv` | `yyyy-MM-dd,processname,seconds` |
| `%AppData%\LoginTimer\overlay.pos` | `x,y` |
| `%AppData%\LoginTimer\error.log` | Appended on unhandled exceptions |

---

## Installer

### MSI (`LoginTimer.msi` — WiX 3.x)
- `InstallScope="perUser"` — no admin required
- Installs to `%LocalAppData%\LoginTimer\`
- Creates Startup shortcut + optional Desktop shortcut
- `MajorUpgrade` — old version removed automatically on reinstall
- `util:CloseApplication Target="LoginTimer.exe" CloseMessage="yes"` — sends `WM_CLOSE` to the running process before replacing the EXE so position/data are saved
- "Launch after install" checkbox on the ExitDialog (`WixShellExec`)

### Fallback installer (`Installer.exe`)
Used when `DisableMSI` Group Policy blocks Windows Installer:
> *"The system administrator has set policies to prevent this installation."*

Detect: `reg query "HKLM\SOFTWARE\Policies\Microsoft\Windows\Installer" /v DisableMSI`

`Installer.exe` is a plain .NET EXE — no Windows Installer involved. Ships alongside
`LoginTimer.exe`; copies it to `%LocalAppData%\LoginTimer\` and creates the Startup shortcut.

### Version bumping (release checklist)
Three files must stay in sync:

| File | Field |
|---|---|
| `src/LoginTimer.cs` | `Program.Version = "x.y.z"` (three-part; feeds menu, assembly attributes, update check) |
| `installer/LoginTimer.wxs` | `Version="x.y.z.0"` (four-part) |
| `CHANGELOG.md` | new `## [x.y.z] — YYYY-MM-DD` section |

Also update the version line in `README.md` (`**Version x.y.z**`).

---

## Project-Specific Commands

| Goal | Command |
|---|---|
| Dev rebuild + restart | `scripts\restart.bat` |
| Full release build | `build-all.bat` |
| Compile EXE only | `scripts\build.bat` |
| Build MSI only | `scripts\build_msi.bat` |
| Compile Installer.exe only | `scripts\build-installer.bat` |
| Compile + run installer | `scripts\install.bat` |

---

## Known constraints and gotchas

- **Single source file** — keep everything in `src/LoginTimer.cs`. Split only if it exceeds ~1 500 lines.
- **`_iconBitmap` field** — the `Bitmap` backing the tray icon must be kept alive as a field. Disposing it invalidates the GDI handle and silently blanks the tray icon.
- **`WS_EX_NOACTIVATE`** on `OverlayForm` prevents focus steal but also means the widget cannot receive keyboard input — intentional.
- **`build_msi.bat` uses `pushd`** — candle/light are run from `installer\` so relative paths in the WXS (`Source="..\dist\LoginTimer.exe"`, `license.rtf`) resolve correctly. The `.wixpdb` is emitted next to the MSI output (`dist\`) not next to the WXS, so cleanup must target `"..\dist\LoginTimer.wixpdb"`.
- **`NOPAUSE` env var** — set to `1` by `build-all.bat` before calling sub-scripts. Sub-scripts guard all `pause` calls with `if not defined NOPAUSE`.
- **PowerShell `Compress-Archive` in bat** — must be on a single line inside `-Command "…"`. `^` line continuation inside a quoted string passed to another process mangles the command.
- **No `.git` in `dist\`** — `dist/*` is gitignored; `dist\.gitkeep` is the only tracked file.
