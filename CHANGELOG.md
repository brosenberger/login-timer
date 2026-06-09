# Changelog

All notable changes to LoginTimer are documented here.  
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.1.0] — 2026-06-09

### Added
- **Per-application time tracking** — every 10 seconds the topmost visible, non-minimised window on each monitor is detected and its process is credited with active time
  - Multi-monitor aware: a video playing on monitor 2 and an editor on monitor 1 are both tracked simultaneously
  - Z-order walk (`GetTopWindow` / `GetWindow`) ensures the correct foreground window per monitor is found even if the global foreground window is on a different screen
  - P/Invoke scan runs on a `ThreadPool` thread so the UI and Chrome/Edge never stutter; results are marshalled back via `SynchronizationContext.Post`
  - System processes (`dwm`, `svchost`, taskbar, desktop, etc.) are automatically excluded
  - Friendly display names for common apps (Chrome, Firefox, Edge, VLC, Spotify, VS Code, …); unknown processes shown with a capitalised process name
  - Data stored in `%AppData%\LoginTimer\apps.csv` (`yyyy-MM-dd,processname,seconds`)
  - Saved every 30 seconds (same checkpoint as login time) and on lock / quit
- **Apps tab** in the History window
  - Shows today's per-application active time sorted by duration
  - Columns: App name, Zeit (HH:MM), Stunden (decimal)
  - "No data yet" placeholder shown until the first 10-second tick completes
- **Widget context menu** — right-clicking the floating widget now shows the same context menu as the tray icon (toggle widget, open history, quit)
- **"LoginTimer jetzt starten" checkbox** on the MSI finish page — opt-in to launch the app immediately after installation
- **`build-all.bat`** (repo root) — one-step build for all artifacts: compiles EXE, builds MSI, compiles `Installer.exe`, then zips all three into `dist\LoginTimer.zip` via PowerShell `Compress-Archive`
- **`scripts\build-installer.bat`** — compile-only step for `Installer.exe` (no auto-launch); called by both `build-all.bat` and `install.bat`
- **Lightweight installer for restricted machines** (`Installer.exe`) documented in README as Option C, for environments where `DisableMSI` Group Policy blocks the MSI

### Fixed
- **Startup time display** — today's tracked seconds are now applied to the overlay immediately on startup rather than waiting for the first 30-second tick
- **NullReferenceException on startup** — `SynchronizationContext.Current` is `null` before `Application.Run` starts; context is now captured lazily on the first app-timer tick
- **Widget position lost on reinstall** — `MajorUpgrade` previously force-killed the process without a clean exit; the MSI now sends `WM_CLOSE` via `util:CloseApplication` so position and data are saved before the upgrade proceeds
- **Installer "Files in Use" list empty** — `OverlayForm` carries `WS_EX_TOOLWINDOW` and has no title, making it invisible to Restart Manager; a hidden `AnchorForm` (normal window, `Text="LoginTimer"`) was added so the installer correctly identifies and closes the running process
- **Re-entrant `Application.Exit()` on close** — `AnchorForm` receiving `WM_CLOSE` from the installer could trigger a second `Application.Exit()` through `ApplicationExit`; guarded with `_exiting` flag and `AllowClose()` on both forms

### Changed
- History window title updated to reflect four tabs (days / weeks / months / **apps**)
- Repo restructured to industry-standard layout: `src/`, `installer/`, `scripts/`, `dist/` — all build artifacts go to `dist/` which is gitignored except for a `.gitkeep` marker
- `scripts\build_msi.bat` and individual build scripts clean their own stale output artifact before each build
- `scripts\build_msi.bat` respects `NOPAUSE` environment variable so it can be called non-interactively from `build-all.bat`
- `scripts\install.bat` delegates compile step to `build-installer.bat` (no duplicated CSC detection)
- DEVELOPER.md updated with deployment decision table, Group Policy detection command, and full `Installer.exe` behaviour description
- All three release artifacts (`LoginTimer.msi`, `LoginTimer.exe`, `Installer.exe`) now attached to GitHub releases

---

## [1.0.0] — 2026-06-09

### Added
- **Auto-tracking** of logged-in time per day via `SystemEvents.SessionSwitch`
  - Pauses on screen lock, remote disconnect, and logoff
  - Resumes on unlock / reconnect
  - Handles midnight rollover correctly (splits segment across two days)
- **System tray icon** with live HH:MM rendered directly into a 32×32 GDI+ bitmap
  - Tooltip shows full label (`LoginTimer  HH:MM heute`)
  - Updates every 30 seconds; also updates on session switch
- **Floating overlay widget**
  - Always-on-top borderless form, semi-transparent dark background, green Consolas text
  - Draggable; position persisted to `%AppData%\LoginTimer\overlay.pos` on every move
  - Stays above the taskbar via `SetWindowPos(HWND_TOPMOST)` re-asserted every second
  - `WS_EX_NOACTIVATE` — clicking the taskbar does not push the widget behind it
  - Toggle via tray context menu
- **History window** (double-click widget or tray icon to open)
  - **Days tab** — last 60 days: date, weekday, duration, decimal hours, ±% vs. previous entry
  - **Weeks tab** — last 16 calendar weeks: total, active days, avg/day, ±% vs. previous week
  - **Months tab** — last 12 months: total, days, avg/day, avg/week, ±% vs. previous month
  - Dark theme DataGridView; positive change green, negative red
  - Window is TopMost — opens above all other windows
- **Data storage** — plain CSV at `%AppData%\LoginTimer\data.csv` (`yyyy-MM-dd,seconds`)
  - Autosave (checkpoint) every 30 seconds
  - Final save on lock / session end / quit
- **Single-instance enforcement** via named Mutex (`LoginTimerMutex_v1`)
- **Error logging** to `%AppData%\LoginTimer\error.log`
- **MSI installer** (WiX 3.x)
  - Per-user install to `%LocalAppData%\LoginTimer\` — no admin required
  - Autostart shortcut in user Startup folder
  - Optional desktop shortcut (off by default)
  - Registered in *Programs and Features* for clean uninstall
  - MajorUpgrade support — future versions replace the previous one automatically
- **Lightweight fallback installer** (`Installer.exe`) — no WiX needed, works standalone
- **Build scripts** — `build.bat` (EXE), `build_msi.bat` (MSI), `restart.bat` (kill → rebuild → restart)
  - Uses `csc.exe` from .NET Framework 4.x — no Visual Studio or SDK required
