using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Version is stamped into the EXE (file properties) from the single
// Program.Version constant below — keep installer/LoginTimer.wxs in sync.
[assembly: AssemblyTitle("LoginTimer")]
[assembly: AssemblyProduct("LoginTimer")]
[assembly: AssemblyVersion(LoginTimer.Program.Version + ".0")]
[assembly: AssemblyFileVersion(LoginTimer.Program.Version + ".0")]

namespace LoginTimer
{
    // ── Win32 structs ─────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    // ── Per-monitor active window detection ───────────────────────────────────
    static class WindowHelper
    {
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
                                      IntPtr lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
                                               MonitorEnumProc lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        static extern IntPtr GetTopWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute,
                                                out int pvAttribute, int cbAttribute);
        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc,
                                            IntPtr lParam);

        const uint MONITOR_DEFAULTTONULL = 0;
        const uint GW_HWNDNEXT           = 2;
        const int  SW_SHOWMINIMIZED      = 2;
        const int  GWL_EXSTYLE           = -20;
        const int  WS_EX_TOOLWINDOW      = 0x00000080;
        const int  WS_EX_NOACTIVATE      = 0x08000000;
        const int  DWMWA_CLOAKED         = 14;
        const int  MinAppWindowSize      = 50; // px; smaller = helper/overlay window

        static readonly int _ownPid = Process.GetCurrentProcess().Id;

        static readonly HashSet<string> _systemProcs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "dwm","winlogon","csrss","wininit","services","lsass","svchost",
                "conhost","dllhost","sihost","fontdrvhost","spoolsv","taskhostw",
                "searchhost","searchindexer","runtimebroker","applicationframehost",
                "shellexperiencehost","startmenuexperiencehost","textinputhost",
                "systemsettings","lockapp","logonui","userinit","idle","registry"
            };

        static readonly HashSet<string> _systemClasses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Shell_TrayWnd","Progman","WorkerW","tooltips_class32",
                "DV2ControlHost","SysShadow","Shell_SecondaryTrayWnd"
            };

        static readonly Dictionary<string, string> _friendlyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"chrome",          "Google Chrome"},
                {"firefox",         "Firefox"},
                {"msedge",          "Microsoft Edge"},
                {"opera",           "Opera"},
                {"brave",           "Brave"},
                {"vivaldi",         "Vivaldi"},
                {"vlc",             "VLC"},
                {"spotify",         "Spotify"},
                {"steam",           "Steam"},
                {"explorer",        "Explorer"},
                {"code",            "VS Code"},
                {"devenv",          "Visual Studio"},
                {"rider64",         "JetBrains Rider"},
                {"phpstorm64",      "PhpStorm"},
                {"idea64",          "IntelliJ IDEA"},
                {"webstorm64",      "WebStorm"},
                {"cursor",          "Cursor"},
                {"slack",           "Slack"},
                {"ms-teams",        "Microsoft Teams"},
                {"teams",           "Microsoft Teams"},
                {"discord",         "Discord"},
                {"zoom",            "Zoom"},
                {"outlook",         "Outlook"},
                {"thunderbird",     "Thunderbird"},
                {"notepad",         "Notepad"},
                {"notepad++",       "Notepad++"},
                {"winword",         "Word"},
                {"excel",           "Excel"},
                {"powerpnt",        "PowerPoint"},
                {"onenote",         "OneNote"},
                {"acrobat",         "Adobe Acrobat"},
                {"acrord32",        "Adobe Reader"},
                {"mspaint",         "Paint"},
                {"gimp-2.10",       "GIMP"},
                {"photoshop",       "Photoshop"},
                {"powershell",      "PowerShell"},
                {"windowsterminal", "Windows Terminal"},
                {"wt",              "Windows Terminal"},
                {"cmd",             "Command Prompt"},
                {"mstsc",           "Remote Desktop"},
                {"putty",           "PuTTY"},
                {"filezilla",       "FileZilla"},
                {"postman",         "Postman"},
                {"calculatorapp",   "Rechner"},
                {"olk",             "Outlook (neu)"},
                {"whatsapp",        "WhatsApp"},
            };

        public static string GetFriendlyName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return "Unknown";
            string friendly;
            if (_friendlyNames.TryGetValue(processName, out friendly)) return friendly;
            return char.ToUpper(processName[0]) + processName.Substring(1);
        }

        /// <summary>
        /// Returns lower-cased process names of the topmost visible,
        /// non-minimised, non-system window on each monitor.
        /// A process visible on N monitors is counted once (HashSet dedup).
        /// </summary>
        public static List<string> GetActiveAppsPerMonitor()
        {
            // 1. Collect monitors
            var monitors = new List<IntPtr>();
            MonitorEnumProc monCb = delegate(IntPtr hMon, IntPtr hdc, IntPtr lprc, IntPtr data)
            {
                monitors.Add(hMon);
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, monCb, IntPtr.Zero);
            if (monitors.Count == 0) return new List<string>();

            // 2. Walk Z-order top-to-bottom, keep visible non-minimised windows
            var zOrder = new List<IntPtr>();
            IntPtr cur = GetTopWindow(IntPtr.Zero);
            int guard = 4000;
            while (cur != IntPtr.Zero && guard-- > 0)
            {
                if (IsWindowVisible(cur))
                {
                    var wp = new WINDOWPLACEMENT();
                    wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                    GetWindowPlacement(cur, ref wp);
                    if (wp.showCmd != SW_SHOWMINIMIZED && IsAppWindow(cur))
                        zOrder.Add(cur);
                }
                cur = GetWindow(cur, GW_HWNDNEXT);
            }

            // 3. For each monitor: first (= topmost) qualifying app window
            var result   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var classBuf = new StringBuilder(256);
            var pidCache = new Dictionary<uint, string>(); // pid -> processName (null = skip)

            foreach (var monitor in monitors)
            {
                foreach (var hwnd in zOrder)
                {
                    if (MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL) != monitor)
                        continue;

                    // Filter system window classes
                    classBuf.Clear();
                    GetClassName(hwnd, classBuf, 256);
                    if (_systemClasses.Contains(classBuf.ToString())) continue;

                    // Get process name (cached per PID)
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if ((int)pid == _ownPid) continue;

                    string procName;
                    if (!pidCache.TryGetValue(pid, out procName))
                    {
                        procName = GetProcessNameSafe((int)pid);
                        pidCache[pid] = procName;
                    }
                    if (procName == null) continue;
                    // Store apps (Rechner, Fotos, ...) run inside an
                    // ApplicationFrameHost frame; the real app owns a child window.
                    if (string.Equals(procName, "ApplicationFrameHost",
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        procName = GetHostedAppName(hwnd, pid);
                        if (procName == null) continue; // suspended / not resolvable
                    }
                    if (_systemProcs.Contains(procName)) continue;

                    result.Add(procName.ToLower());
                    break; // topmost app found for this monitor
                }
            }
            return new List<string>(result);
        }

        // Filters windows that are "visible" but no app the user looks at:
        // tool/overlay windows (e.g. a vendor OSD: 1x1, topmost, no-activate),
        // and cloaked windows (other virtual desktops, suspended Store-app
        // frames). Without this a topmost overlay wins every monitor.
        static bool IsAppWindow(IntPtr hWnd)
        {
            int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return false;

            int cloaked;
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 &&
                cloaked != 0) return false;

            RECT r;
            if (!GetWindowRect(hWnd, out r)) return false;
            return r.Right - r.Left >= MinAppWindowSize && r.Bottom - r.Top >= MinAppWindowSize;
        }

        static string GetHostedAppName(IntPtr frame, uint framePid)
        {
            string name = null;
            EnumChildWindows(frame, delegate(IntPtr child, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(child, out pid);
                if (pid == 0 || pid == framePid) return true;
                name = GetProcessNameSafe((int)pid);
                return name == null; // stop at the first resolvable app
            }, IntPtr.Zero);
            return name;
        }

        static string GetProcessNameSafe(int pid)
        {
            try { using (var p = Process.GetProcessById(pid)) return p.ProcessName; }
            catch { return null; }
        }
    }

    // ── Color scheme (persisted to colors.ini) ────────────────────────────────
    class ColorScheme
    {
        public Color IconBackground  { get; set; }
        public Color AccentGreen     { get; set; }
        public Color OverlayBg       { get; set; }
        public Color HistoryBg       { get; set; }
        public Color GridBg          { get; set; }
        public Color GridLines       { get; set; }
        public Color HeaderBg        { get; set; }
        public Color CellText        { get; set; }
        public Color HeaderText      { get; set; }
        public Color FormText        { get; set; }
        public Color PlaceholderText { get; set; }
        public Color SelectionBg     { get; set; }
        public Color NegativeRed     { get; set; }

        public static ColorScheme Default()
        {
            return new ColorScheme
            {
                IconBackground  = Color.FromArgb(22,  22,  22),
                AccentGreen     = Color.FromArgb(0,  215,  85),
                OverlayBg       = Color.FromArgb(18,  18,  18),
                HistoryBg       = Color.FromArgb(28,  28,  28),
                GridBg          = Color.FromArgb(35,  35,  35),
                GridLines       = Color.FromArgb(55,  55,  55),
                HeaderBg        = Color.FromArgb(45,  45,  45),
                CellText        = Color.FromArgb(210, 210, 210),
                HeaderText      = Color.FromArgb(160, 160, 160),
                FormText        = Color.FromArgb(200, 200, 200),
                PlaceholderText = Color.FromArgb(120, 120, 120),
                SelectionBg     = Color.FromArgb(55,  100,  70),
                NegativeRed     = Color.FromArgb(220,  80,  60),
            };
        }

        static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LoginTimer", "colors.ini");

        static string ColorToStr(Color c) { return c.R + "," + c.G + "," + c.B; }

        static Color StrToColor(string s, Color fallback)
        {
            try
            {
                var p = s.Split(',');
                if (p.Length == 3)
                    return Color.FromArgb(int.Parse(p[0].Trim()),
                                          int.Parse(p[1].Trim()),
                                          int.Parse(p[2].Trim()));
            }
            catch { }
            return fallback;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var sb = new StringBuilder();
                sb.AppendLine("IconBackground="  + ColorToStr(IconBackground));
                sb.AppendLine("AccentGreen="     + ColorToStr(AccentGreen));
                sb.AppendLine("OverlayBg="       + ColorToStr(OverlayBg));
                sb.AppendLine("HistoryBg="       + ColorToStr(HistoryBg));
                sb.AppendLine("GridBg="          + ColorToStr(GridBg));
                sb.AppendLine("GridLines="       + ColorToStr(GridLines));
                sb.AppendLine("HeaderBg="        + ColorToStr(HeaderBg));
                sb.AppendLine("CellText="        + ColorToStr(CellText));
                sb.AppendLine("HeaderText="      + ColorToStr(HeaderText));
                sb.AppendLine("FormText="        + ColorToStr(FormText));
                sb.AppendLine("PlaceholderText=" + ColorToStr(PlaceholderText));
                sb.AppendLine("SelectionBg="     + ColorToStr(SelectionBg));
                sb.AppendLine("NegativeRed="     + ColorToStr(NegativeRed));
                File.WriteAllText(_path, sb.ToString());
            }
            catch { }
        }

        public static ColorScheme Load()
        {
            var d = Default();
            if (!File.Exists(_path)) return d;
            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(_path))
                {
                    var idx = line.IndexOf('=');
                    if (idx > 0)
                        map[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                }
                string v;
                if (map.TryGetValue("IconBackground",  out v)) d.IconBackground  = StrToColor(v, d.IconBackground);
                if (map.TryGetValue("AccentGreen",     out v)) d.AccentGreen     = StrToColor(v, d.AccentGreen);
                if (map.TryGetValue("OverlayBg",       out v)) d.OverlayBg       = StrToColor(v, d.OverlayBg);
                if (map.TryGetValue("HistoryBg",       out v)) d.HistoryBg       = StrToColor(v, d.HistoryBg);
                if (map.TryGetValue("GridBg",          out v)) d.GridBg          = StrToColor(v, d.GridBg);
                if (map.TryGetValue("GridLines",       out v)) d.GridLines       = StrToColor(v, d.GridLines);
                if (map.TryGetValue("HeaderBg",        out v)) d.HeaderBg        = StrToColor(v, d.HeaderBg);
                if (map.TryGetValue("CellText",        out v)) d.CellText        = StrToColor(v, d.CellText);
                if (map.TryGetValue("HeaderText",      out v)) d.HeaderText      = StrToColor(v, d.HeaderText);
                if (map.TryGetValue("FormText",        out v)) d.FormText        = StrToColor(v, d.FormText);
                if (map.TryGetValue("PlaceholderText", out v)) d.PlaceholderText = StrToColor(v, d.PlaceholderText);
                if (map.TryGetValue("SelectionBg",     out v)) d.SelectionBg     = StrToColor(v, d.SelectionBg);
                if (map.TryGetValue("NegativeRed",     out v)) d.NegativeRed     = StrToColor(v, d.NegativeRed);
            }
            catch { }
            return d;
        }
    }

    // ── User settings (persisted to settings.ini) ────────────────────────────
    class AppSettings
    {
        public int    IdleMinutes;       // pause after n minutes without input; 0 = off
        public double WeeklyTargetHours; // weekly target for the Weeks tab; 0 = off
        public bool   OverlayVisible;

        static string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LoginTimer", "settings.ini");

        public static AppSettings Load()
        {
            var s = new AppSettings { IdleMinutes = 0, WeeklyTargetHours = 40, OverlayVisible = true };
            try
            {
                if (!File.Exists(_path)) return s;
                foreach (var line in Storage.ReadAllLinesShared(_path))
                {
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = line.Substring(0, idx).Trim();
                    var val = line.Substring(idx + 1).Trim();
                    int i;
                    double d;
                    if (key == "IdleMinutes" && int.TryParse(val, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out i))
                        s.IdleMinutes = Math.Max(0, i);
                    else if (key == "WeeklyTargetHours" && double.TryParse(val, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out d))
                        s.WeeklyTargetHours = Math.Max(0, d);
                    else if (key == "OverlayVisible")
                        s.OverlayVisible = val != "0";
                }
            }
            catch (Exception ex) { Program.LogError("settings.ini konnte nicht gelesen werden:\n" + ex); }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                Storage.WriteAllLinesAtomic(_path, new[]
                {
                    "IdleMinutes=" + IdleMinutes.ToString(CultureInfo.InvariantCulture),
                    "WeeklyTargetHours=" + WeeklyTargetHours.ToString(CultureInfo.InvariantCulture),
                    "OverlayVisible=" + (OverlayVisible ? "1" : "0"),
                });
            }
            catch (Exception ex) { Program.LogError("settings.ini konnte nicht gespeichert werden:\n" + ex); }
        }
    }

    // ── Per-app time storage ──────────────────────────────────────────────────
    class AppTracker
    {
        readonly string _path;
        // date → processName → accumulated seconds
        Dictionary<DateTime, Dictionary<string, double>> _data;

        bool _loaded; // false → apps.csv unreadable; Save() must not overwrite it

        public bool Loaded { get { return _loaded; } }

        public AppTracker(string path)
        {
            _path = path;
            _data = new Dictionary<DateTime, Dictionary<string, double>>();
            try { Load(); }
            catch (Exception ex) { Program.LogError("apps.csv konnte nicht gelesen werden:\n" + ex); }
        }

        public void RecordTick(IEnumerable<string> processNames, double seconds)
        {
            var today = DateTime.Today;
            if (!_data.ContainsKey(today))
                _data[today] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in processNames)
            {
                if (!_data[today].ContainsKey(name))
                    _data[today][name] = 0.0;
                _data[today][name] += seconds;
            }
        }

        // Summed seconds per process over the days from..to (inclusive).
        public Dictionary<string, double> GetRange(DateTime from, DateTime to)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var day in _data)
            {
                if (day.Key < from.Date || day.Key > to.Date) continue;
                foreach (var app in day.Value)
                {
                    double cur;
                    result.TryGetValue(app.Key, out cur);
                    result[app.Key] = cur + app.Value;
                }
            }
            return result;
        }

        // Throws on failure (caller retries). While apps.csv could not be read,
        // retries the load first: writing now would wipe the stored history.
        public void Save()
        {
            if (!_loaded) Load();
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var lines = new List<string>();
            foreach (var day in _data.OrderBy(kv => kv.Key))
                foreach (var app in day.Value.OrderByDescending(kv => kv.Value))
                    lines.Add(day.Key.ToString("yyyy-MM-dd") + "," +
                               app.Key + "," +
                               app.Value.ToString(CultureInfo.InvariantCulture));
            Storage.WriteAllLinesAtomic(_path, lines);
        }

        // Adds the file's totals to what is already in memory (empty at
        // startup; the time tracked so far when a failed load is retried).
        void Load()
        {
            if (!File.Exists(_path)) { _loaded = true; return; }
            var parsed = new Dictionary<DateTime, Dictionary<string, double>>();
            foreach (var line in Storage.ReadAllLinesShared(_path))
            {
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                DateTime d;
                double s;
                if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                var name = parts[1].Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (!double.TryParse(parts[2].Trim(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out s)) continue;
                if (!parsed.ContainsKey(d.Date))
                    parsed[d.Date] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                parsed[d.Date][name] = s;
            }
            foreach (var day in parsed)
            {
                if (!_data.ContainsKey(day.Key))
                    _data[day.Key] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var target = _data[day.Key];
                foreach (var app in day.Value)
                {
                    double cur;
                    target.TryGetValue(app.Key, out cur);
                    target[app.Key] = cur + app.Value;
                }
            }
            _loaded = true;
        }
    }

    // ── Update check + EXE self-update via GitHub releases ───────────────────
    class UpdateInfo
    {
        public string Version; // "" = up to date, "x.y.z" = newer available
        public string ExeUrl;  // LoginTimer.exe asset URL (null = not in release)
        public string Sha256;  // expected asset digest (null = not provided)
    }

    static class UpdateChecker
    {
        public const string ReleasesPage =
            "https://github.com/brosenberger/login-timer/releases/latest";
        const string ApiUrl =
            "https://api.github.com/repos/brosenberger/login-timer/releases/latest";

        /// <summary>
        /// Fetches the latest release on a thread-pool thread.
        /// Callback (raised on the worker thread — marshal in the caller):
        /// null = check failed; Version == "" = up to date; otherwise a newer
        /// version, with ExeUrl/Sha256 set when the release ships LoginTimer.exe.
        /// </summary>
        public static void CheckAsync(Action<UpdateInfo> onResult)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                onResult(FetchLatest());
            });
        }

        static UpdateInfo FetchLatest()
        {
            try
            {
                // GitHub enforces TLS 1.2; .NET 4.x defaults may not enable it.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                var req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.UserAgent = "LoginTimer/" + Program.Version;
                req.Accept    = "application/vnd.github+json";
                req.Timeout   = 10000;
                string json;
                using (var resp = req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    json = reader.ReadToEnd();

                var m = Regex.Match(json,
                    "\"tag_name\"\\s*:\\s*\"v?([0-9]+(?:\\.[0-9]+)+)\"");
                if (!m.Success) return null;
                var latest = m.Groups[1].Value;
                if (!IsNewer(latest, Program.Version))
                    return new UpdateInfo { Version = "" };

                var info = new UpdateInfo { Version = latest };
                // Within an asset object the fields appear in the order
                // name → digest → browser_download_url, so scoping the search
                // to the substring after our asset's name keeps both matches
                // on the right asset.
                var nameMatch = Regex.Match(json,
                    "\"name\"\\s*:\\s*\"LoginTimer\\.exe\"");
                if (nameMatch.Success)
                {
                    var tail = json.Substring(nameMatch.Index);
                    var dm = Regex.Match(tail,
                        "\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"");
                    if (dm.Success) info.Sha256 = dm.Groups[1].Value;
                    var um = Regex.Match(tail,
                        "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
                    if (um.Success) info.ExeUrl = um.Groups[1].Value;
                }
                return info;
            }
            catch { return null; }
        }

        static bool IsNewer(string remote, string local)
        {
            try { return new Version(remote) > new Version(local); }
            catch { return false; }
        }

        /// <summary>
        /// Downloads the EXE asset next to the running EXE, verifies the
        /// digest, then swaps it in: a running EXE is locked against writes
        /// but CAN be renamed, so current → .old, new → current.
        /// Throws on failure (download dir cleaned up, swap rolled back).
        /// </summary>
        public static void DownloadAndSwap(UpdateInfo info)
        {
            // Never run an unverified download: no digest → manual update only.
            if (info.Sha256 == null)
                throw new InvalidOperationException(
                    "Das Release enthält keine Prüfsumme für LoginTimer.exe.");

            var exePath = Application.ExecutablePath;
            var newPath = Path.Combine(Path.GetDirectoryName(exePath),
                                       "LoginTimer.exe.new");
            var oldPath = exePath + ".old";

            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "LoginTimer/" + Program.Version;
                wc.DownloadFile(info.ExeUrl, newPath);
            }

            if (!string.Equals(Sha256Of(newPath), info.Sha256,
                               StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(newPath); } catch { }
                throw new InvalidOperationException(
                    "Checksumme der heruntergeladenen Datei stimmt nicht.");
            }

            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exePath, oldPath);
            try { File.Move(newPath, exePath); }
            catch { File.Move(oldPath, exePath); throw; } // rollback
        }

        /// <summary>
        /// False when the EXE folder is not writable for this user (e.g. a
        /// "for all users" install under Program Files): self-update would
        /// fail there, so only the manual download is offered.
        /// </summary>
        public static bool CanSelfUpdate()
        {
            var probe = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),
                                     "LoginTimer.write-test");
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Removes the .old EXE left behind by a previous self-update.
        /// Retries in the background: right after an update the exiting old
        /// instance may still hold the file lock for a moment.
        /// </summary>
        public static void CleanupOldExeAsync()
        {
            var oldPath = Application.ExecutablePath + ".old";
            if (!File.Exists(oldPath)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < 10; i++)
                {
                    try { File.Delete(oldPath); return; }
                    catch { Thread.Sleep(1000); }
                }
            });
        }

        static string Sha256Of(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    // ── Entry point ───────────────────────────────────────────────────────────
    static class Program
    {
        // Single source of truth for the app version.
        // Release checklist: keep installer/LoginTimer.wxs Version="x.y.z.0",
        // CHANGELOG.md and README.md in sync (see AGENTS.md).
        public const string Version = "1.3.0";

        // Not initially owned: WaitOne in Main is the only acquisition, so the
        // single ReleaseMutex at exit really frees it (the --updated instance
        // gets it cleanly instead of via AbandonedMutexException).
        static Mutex _mutex = new Mutex(false, "LoginTimerMutex_v1");

        static readonly string ErrorLog =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LoginTimer", "error.log");

        [STAThread]
        static void Main()
        {
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // After a self-update the old instance is still shutting down —
            // wait for the mutex instead of bailing out immediately.
            bool afterUpdate =
                Environment.GetCommandLineArgs().Contains("--updated");
            bool gotMutex;
            try { gotMutex = _mutex.WaitOne(afterUpdate ? 15000 : 0, true); }
            catch (AbandonedMutexException) { gotMutex = true; } // holder died — mutex is ours

            if (!gotMutex)
            {
                MessageBox.Show("LoginTimer läuft bereits in der Taskleiste.",
                    "LoginTimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            UpdateChecker.CleanupOldExeAsync();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new TrayApp());
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                MessageBox.Show("Fehler beim Starten:\n" + ex.Message, "LoginTimer",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _mutex.ReleaseMutex();
        }

        static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            LogError(e.Exception.ToString());
            MessageBox.Show("Fehler:\n" + e.Exception.Message, "LoginTimer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogError(e.ExceptionObject.ToString());
        }

        internal static void LogError(string msg)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(ErrorLog);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(ErrorLog, DateTime.Now + ":\n" + msg + "\n\n");
            }
            catch { }
        }
    }

    // ── Shared file access ────────────────────────────────────────────────────
    static class Storage
    {
        // File.ReadAllLines denies sharing with writers, so it fails while the
        // CSV is open in Excel (or locked by a sync client). Excel keeps the
        // file open for writing but lets others read it.
        public static string[] ReadAllLinesShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs))
            {
                var lines = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
                return lines.ToArray();
            }
        }

        // Write to a temp file, then swap it in. A crash or power loss during
        // the write can no longer leave the CSV empty or cut off (which the
        // next save would then make permanent). Keeps the previous version
        // as <file>.bak for manual recovery.
        public static void WriteAllLinesAtomic(string path, IEnumerable<string> lines)
        {
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
    }

    // ── Storage: simple CSV  date,seconds ─────────────────────────────────────
    class DayData
    {
        readonly Dictionary<DateTime, double> _secs = new Dictionary<DateTime, double>();
        readonly string _path;

        bool _loaded; // false → data.csv unreadable; Save() must not overwrite it

        public bool Loaded { get { return _loaded; } }

        public DayData(string path)
        {
            _path = path;
            try { Load(); }
            catch (Exception ex) { Program.LogError("data.csv konnte nicht gelesen werden:\n" + ex); }
        }

        // Adds the file's totals to what is already in memory (empty at
        // startup; the time tracked so far when a failed load is retried).
        void Load()
        {
            if (!File.Exists(_path)) { _loaded = true; return; }
            var parsed = new Dictionary<DateTime, double>();
            foreach (var line in Storage.ReadAllLinesShared(_path))
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                DateTime d;
                double s;
                if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                if (!double.TryParse(parts[1].Trim(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out s)) continue;
                parsed[d.Date] = s;
            }
            foreach (var kv in parsed)
                _secs[kv.Key] = Get(kv.Key) + kv.Value;
            _loaded = true;
        }

        // Throws on failure (caller retries). While data.csv could not be read,
        // retries the load first: writing now would wipe the stored history.
        public void Save()
        {
            if (!_loaded) Load();
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var lines = _secs
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Key.ToString("yyyy-MM-dd") + "," +
                              kv.Value.ToString(CultureInfo.InvariantCulture));
            Storage.WriteAllLinesAtomic(_path, lines);
        }

        public double Get(DateTime day)
        {
            return _secs.ContainsKey(day.Date) ? _secs[day.Date] : 0.0;
        }

        public void Set(DateTime day, double seconds)
        {
            _secs[day.Date] = Math.Max(0, seconds);
        }

        public IEnumerable<KeyValuePair<DateTime, double>> All()
        {
            return _secs.OrderByDescending(kv => kv.Key);
        }
    }

    // ── Tray application ──────────────────────────────────────────────────────
    class TrayApp : ApplicationContext
    {
        readonly string _dataPath;
        DayData     _data;
        AppTracker  _appTracker;
        DateTime?   _segStart;
        NotifyIcon  _tray;
        System.Windows.Forms.Timer _timer;       // 30 s: checkpoint + icon update
        System.Windows.Forms.Timer _appTimer;    // 10 s: per-monitor app tracking
        System.Windows.Forms.Timer _updateTimer; // one-shot: silent update check after startup
        Icon        _currentIcon; // wraps an HICON we own — free via DestroyOwnedIcon
        Bitmap      _iconBitmap;  // must stay alive while icon handle is in use
        OverlayForm _overlay;
        AnchorForm  _anchor;      // hidden window visible to Restart Manager / WixCloseApplications
        System.Threading.SynchronizationContext _syncCtx; // marshal back to UI thread
        bool        _exiting;     // guard against re-entrant OnExit calls
        ColorScheme _colors;
        UpdateInfo  _pendingUpdate; // set by silent check; consumed by balloon click
        DateTime    _lastAlive;     // last timer tick — detects sleep/hibernate gaps
        bool        _saveFailing;   // log a save failure once, not every 30 s
        AppSettings _settings;
        bool        _idlePaused;    // segment stopped by idle detection (not by lock)

        static readonly TimeSpan MaxTickGap = TimeSpan.FromSeconds(60);

        public TrayApp()
        {
            _colors   = ColorScheme.Load();
            _settings = AppSettings.Load();

            _dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LoginTimer", "data.csv");

            _data = new DayData(_dataPath);

            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LoginTimer", "apps.csv");
            _appTracker = new AppTracker(appDataPath);

            _segStart  = DateTime.Now;
            _lastAlive = _segStart.Value;
            // _syncCtx is captured lazily on the first timer tick (see OnAppTick),
            // because SynchronizationContext.Current is null here — Application.Run
            // installs the WinForms context only after the constructor returns.

            // Hidden anchor window: makes Restart Manager and WixCloseApplications
            // able to identify LoginTimer by name and send a graceful WM_CLOSE.
            _anchor = new AnchorForm();
            _anchor.Show();

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = BuildMenu();
            _tray.DoubleClick += OnDoubleClick;
            _tray.BalloonTipClicked += OnBalloonClicked;
            _tray.Text = "LoginTimer";

            UpdateIcon();
            _tray.Visible = true;

            if (!_data.Loaded || !_appTracker.Loaded)
            {
                _tray.BalloonTipTitle = "LoginTimer - Verlauf nicht lesbar";
                _tray.BalloonTipText  =
                    "Die Datendatei ist gesperrt oder beschädigt (Details in error.log). " +
                    "Die Zeit wird weiter gezählt und gespeichert, sobald die Datei lesbar ist.";
                _tray.BalloonTipIcon  = ToolTipIcon.Warning;
                _tray.ShowBalloonTip(10000);
            }

            _overlay = new OverlayForm(_colors);
            _overlay.ContextMenuStrip = _tray.ContextMenuStrip;
            _overlay.RequestHistory += delegate { ShowHistory(); };
            if (_settings.OverlayVisible) _overlay.Show();
            _overlay.SetTime(FormatHM(TodaySeconds())); // show correct time immediately

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 30000;
            _timer.Tick += OnTick;
            _timer.Start();

            _appTimer = new System.Windows.Forms.Timer();
            _appTimer.Interval = 10000;
            _appTimer.Tick += OnAppTick;
            _appTimer.Start();

            // One-shot, delayed so startup stays fast and the WinForms
            // SynchronizationContext is installed by the time it fires.
            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = 15000;
            _updateTimer.Tick += OnUpdateTimerTick;
            _updateTimer.Start();

            SystemEvents.SessionSwitch  += OnSessionSwitch;
            SystemEvents.SessionEnding  += OnSessionEnding;
            Application.ApplicationExit += OnExit;
        }

        void OnDoubleClick(object sender, EventArgs e) { ShowHistory(); }

        void OnTick(object sender, EventArgs e)
        {
            Checkpoint();
            UpdateIcon();
        }

        void OnAppTick(object sender, EventArgs e)
        {
            Heartbeat();
            CheckIdle(IdleSeconds());
            if (_segStart == null) return; // locked / disconnected / idle

            // Lazy capture: Application.Run installs the WinForms sync context
            // only after the constructor returns, so we grab it here on the first tick.
            if (_syncCtx == null)
                _syncCtx = System.Threading.SynchronizationContext.Current;
            var ctx = _syncCtx;
            if (ctx == null) return; // not ready yet, skip this tick

            // Run the P/Invoke scan on a thread-pool thread so the UI stays
            // responsive (window dragging, Chrome tab switching, etc.).
            double tickSecs = _appTimer.Interval / 1000.0;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var apps = WindowHelper.GetActiveAppsPerMonitor();
                if (apps.Count == 0) return;
                ctx.Post(delegate
                {
                    // Back on UI thread: safe to mutate AppTracker
                    if (_segStart != null)
                        _appTracker.RecordTick(apps, tickSecs);
                }, null);
            });
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            // Save but keep counting: another app may still cancel the
            // logoff/shutdown. If it goes through, OnExit commits the rest.
            Checkpoint();
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock ||
                e.Reason == SessionSwitchReason.SessionLogoff ||
                e.Reason == SessionSwitchReason.RemoteDisconnect)
            {
                CommitSegment();
                _idlePaused = false; // lock ends an idle pause; unlock restarts
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock ||
                     e.Reason == SessionSwitchReason.SessionLogon ||
                     e.Reason == SessionSwitchReason.RemoteConnect)
            {
                Heartbeat();
                _idlePaused = false;
                if (_segStart == null)
                    _segStart = DateTime.Now;
            }
            UpdateIcon();
        }

        // Timers do not fire while the machine sleeps or hibernates. A gap
        // longer than MaxTickGap since the last timer tick means the time in
        // between was not spent logged in: book the running segment up to the
        // last sign of life and restart it now. Must run before any booking.
        void Heartbeat()
        {
            var now = DateTime.Now;
            if (_segStart != null && now - _lastAlive > MaxTickGap)
            {
                Accumulate(_lastAlive);
                _segStart = now;
            }
            _lastAlive = now;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        // Seconds since the last keyboard/mouse input in this session.
        static double IdleSeconds()
        {
            var lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref lii)) return 0;
            uint ms = unchecked((uint)Environment.TickCount - lii.dwTime); // wraps after 49 days
            return ms / 1000.0;
        }

        // Opt-in (settings: IdleMinutes > 0). After the threshold without
        // input the segment is booked up to the last input and stopped; the
        // next input restarts it at the moment of that input.
        void CheckIdle(double idleSecs)
        {
            double threshold = _settings.IdleMinutes * 60.0;
            if (threshold > 0 && _segStart != null && idleSecs >= threshold)
            {
                Accumulate(DateTime.Now.AddSeconds(-idleSecs));
                _segStart   = null;
                _idlePaused = true;
                SaveAll();
                UpdateIcon();
            }
            else if (_idlePaused && (threshold <= 0 || idleSecs < threshold))
            {
                _idlePaused = false;
                _segStart   = DateTime.Now.AddSeconds(-Math.Min(idleSecs, MaxTickGap.TotalSeconds));
                UpdateIcon();
            }
        }

        // Books _segStart..end into the per-day totals (split at midnight)
        // and moves _segStart to end. In memory only — see SaveAll().
        void Accumulate(DateTime end)
        {
            var start = _segStart.Value;
            while (start < end)
            {
                var nextMidnight = start.Date.AddDays(1);
                var chunkEnd = end < nextMidnight ? end : nextMidnight;
                _data.Set(start.Date, _data.Get(start.Date) + (chunkEnd - start).TotalSeconds);
                start = chunkEnd;
            }
            _segStart = start;
        }

        // Autosave: book elapsed time, keep the segment running
        void Checkpoint()
        {
            Heartbeat();
            if (_segStart == null) return;
            Accumulate(DateTime.Now);
            SaveAll();
        }

        // Final commit on lock / exit
        void CommitSegment()
        {
            Heartbeat();
            if (_segStart == null) return;
            Accumulate(DateTime.Now);
            _segStart = null;
            SaveAll();
        }

        // Never throws: the in-memory totals are already booked, so a failed
        // write (file open in Excel, sync client lock, ...) is simply retried
        // on the next checkpoint. Logged once per failure streak.
        void SaveAll()
        {
            Exception error = null;
            try { _data.Save(); } catch (Exception ex) { error = ex; }
            try { _appTracker.Save(); } catch (Exception ex) { if (error == null) error = ex; }
            if (error == null) { _saveFailing = false; return; }
            if (!_saveFailing)
                Program.LogError("Speichern fehlgeschlagen, wird beim nächsten Checkpoint erneut versucht:\n" + error);
            _saveFailing = true;
        }

        double TodaySeconds()
        {
            var saved = _data.Get(DateTime.Today);
            double live = 0;
            if (_segStart.HasValue)
                live = Math.Max(0, (DateTime.Now - _segStart.Value).TotalSeconds);
            return saved + live;
        }

        void UpdateIcon()
        {
            Heartbeat(); // drop a sleep gap before showing the live total
            var secs  = TodaySeconds();
            var label = FormatHM(secs);
            _tray.Text = "LoginTimer  " + label + " heute" +
                         (_idlePaused ? " (pausiert, inaktiv)" : "");

            var oldIcon = _currentIcon;
            _currentIcon = MakeIcon(label);
            _tray.Icon = _currentIcon;
            DestroyOwnedIcon(oldIcon);

            if (_overlay != null) _overlay.SetTime(label);
        }

        static string FormatHM(double seconds)
        {
            var h = (int)seconds / 3600;
            var m = ((int)seconds % 3600) / 60;
            return string.Format("{0:D2}:{1:D2}", h, m);
        }

        Icon MakeIcon(string label)
        {
            const int S = 32;
            var bmp = new Bitmap(S, S);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(_colors.IconBackground);
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                var parts = label.Split(':');
                var green = new SolidBrush(_colors.AccentGreen);
                var sfC   = new StringFormat();
                sfC.Alignment     = StringAlignment.Center;
                sfC.LineAlignment = StringAlignment.Center;

                using (var fH = new Font("Arial", 14f, FontStyle.Bold))
                    g.DrawString(parts[0], fH, green, new RectangleF(0, -3, S, S * 0.65f), sfC);

                using (var fM = new Font("Arial", 7f, FontStyle.Regular))
                    g.DrawString(parts[1], fM, green, new RectangleF(0, S * 0.58f, S, S * 0.42f), sfC);

                green.Dispose();
                sfC.Dispose();
            }
            if (_iconBitmap != null) _iconBitmap.Dispose();
            _iconBitmap = bmp;
            // The HICON from GetHicon() is NOT owned by the Icon wrapper:
            // Icon.Dispose() does not free it, so DestroyOwnedIcon must.
            return Icon.FromHandle(bmp.GetHicon());
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        static void DestroyOwnedIcon(Icon icon)
        {
            if (icon == null) return;
            var handle = icon.Handle;
            icon.Dispose();
            DestroyIcon(handle);
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Verlauf anzeigen", null, OnShowHistory);
            m.Items.Add("Farben…", null, OnFarben);
            m.Items.Add("Einstellungen…", null, OnSettings);
            m.Items.Add(new ToolStripSeparator());
            var versionItem = new ToolStripMenuItem("Version " + Program.Version);
            versionItem.Enabled = false;
            m.Items.Add(versionItem);
            m.Items.Add("Auf Updates prüfen...", null, OnCheckUpdates);
            m.Items.Add(new ToolStripSeparator());
            var overlayItem = new ToolStripMenuItem("Zeitanzeige (Widget)");
            overlayItem.Checked = _settings.OverlayVisible;
            overlayItem.Click  += OnToggleOverlay;
            m.Items.Add(overlayItem);
            m.Items.Add("Beenden", null, OnQuit);
            return m;
        }

        void OnUpdateTimerTick(object sender, EventArgs e)
        {
            _updateTimer.Stop(); // one-shot
            RunUpdateCheck(false);
        }

        void OnCheckUpdates(object sender, EventArgs e) { RunUpdateCheck(true); }

        // interactive=true → report every outcome (dialog);
        // interactive=false → balloon only when an update exists.
        void RunUpdateCheck(bool interactive)
        {
            var ctx = System.Threading.SynchronizationContext.Current;
            if (ctx == null) return;
            UpdateChecker.CheckAsync(delegate(UpdateInfo info)
            {
                ctx.Post(delegate
                {
                    if (_exiting) return;
                    OnUpdateResult(info, interactive);
                }, null);
            });
        }

        void OnUpdateResult(UpdateInfo info, bool interactive)
        {
            if (info != null && info.Version.Length > 0)
            {
                if (interactive)
                {
                    OfferUpdate(info);
                }
                else
                {
                    _pendingUpdate = info;
                    _tray.BalloonTipTitle = "LoginTimer - Update verfügbar";
                    _tray.BalloonTipText  = string.Format(
                        "Version {0} ist verfügbar (installiert: {1}). " +
                        "Klicken für Details.",
                        info.Version, Program.Version);
                    _tray.BalloonTipIcon  = ToolTipIcon.Info;
                    _tray.ShowBalloonTip(10000);
                }
                return;
            }

            if (!interactive) return;

            if (info == null)
                MessageBox.Show(
                    "Update-Prüfung fehlgeschlagen (keine Verbindung zu GitHub?).",
                    "LoginTimer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(
                    string.Format("LoginTimer {0} ist aktuell.", Program.Version),
                    "LoginTimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OnBalloonClicked(object sender, EventArgs e)
        {
            var info = _pendingUpdate;
            if (info != null) OfferUpdate(info);
        }

        void OfferUpdate(UpdateInfo info)
        {
            if (info.ExeUrl == null || info.Sha256 == null || !UpdateChecker.CanSelfUpdate())
            {
                // No verifiable EXE asset, or install folder not writable
                // → manual download only.
                var a = MessageBox.Show(
                    string.Format(
                        "Version {0} ist verfügbar (installiert: {1}).\n\n" +
                        "Release-Seite jetzt öffnen?",
                        info.Version, Program.Version),
                    "LoginTimer - Update verfügbar",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (a == DialogResult.Yes) OpenReleasesPage();
                return;
            }

            var answer = MessageBox.Show(
                string.Format(
                    "Version {0} ist verfügbar (installiert: {1}).\n\n" +
                    "Ja        = jetzt aktualisieren und neu starten\n" +
                    "Nein      = Release-Seite im Browser öffnen\n" +
                    "Abbrechen = später",
                    info.Version, Program.Version),
                "LoginTimer - Update verfügbar",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.No)  { OpenReleasesPage(); return; }
            if (answer != DialogResult.Yes) return;
            StartSelfUpdate(info);
        }

        void StartSelfUpdate(UpdateInfo info)
        {
            var ctx = System.Threading.SynchronizationContext.Current;
            if (ctx == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { UpdateChecker.DownloadAndSwap(info); }
                catch (Exception ex) { error = ex.Message; }
                ctx.Post(delegate
                {
                    if (_exiting) return;
                    if (error != null)
                    {
                        var a = MessageBox.Show(
                            "Update fehlgeschlagen: " + error + "\n\n" +
                            "Release-Seite für manuellen Download öffnen?",
                            "LoginTimer", MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (a == DialogResult.Yes) OpenReleasesPage();
                        return;
                    }
                    // New EXE waits (--updated) until this instance has
                    // exited and released the mutex; exit commits all data.
                    try
                    {
                        Process.Start(Application.ExecutablePath, "--updated");
                    }
                    catch { }
                    Application.Exit();
                }, null);
            });
        }

        static void OpenReleasesPage()
        {
            try { Process.Start(UpdateChecker.ReleasesPage); } catch { }
        }

        void OnShowHistory(object sender, EventArgs e) { ShowHistory(); }

        void OnToggleOverlay(object sender, EventArgs e)
        {
            if (_overlay.Visible) _overlay.Hide(); else _overlay.Show();
            _settings.OverlayVisible = _overlay.Visible;
            _settings.Save();
            foreach (ToolStripItem item in _tray.ContextMenuStrip.Items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null && mi.Text.StartsWith("Zeitanzeige"))
                    mi.Checked = _overlay.Visible;
            }
        }

        void OnQuit(object sender, EventArgs e)
        {
            // Defer so the context-menu click handling finishes before we
            // dispose the tray and its shared ContextMenuStrip in OnExit.
            _tray.ContextMenuStrip.BeginInvoke(new Action(() => Application.Exit()));
        }

        void OnFarben(object sender, EventArgs e)
        {
            using (var dlg = new ColorSettingsForm(_colors))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                _colors = dlg.Result;
                _colors.Save();
                UpdateIcon();
                _overlay.ApplyColors(_colors);

                // An open history window is rebuilt with the new colors.
                if (OpenHistory() != null) ShowHistory();
            }
        }

        void OnSettings(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm(_settings))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                _settings.IdleMinutes       = dlg.IdleMinutes;
                _settings.WeeklyTargetHours = dlg.WeeklyTargetHours;
                _settings.Save();
                CheckIdle(IdleSeconds()); // turning idle detection off resumes a pause
                if (OpenHistory() != null) ShowHistory();
            }
        }

        static HistoryForm OpenHistory()
        {
            return Application.OpenForms.Cast<Form>().OfType<HistoryForm>().FirstOrDefault();
        }

        void ShowHistory()
        {
            Heartbeat();
            var snap = new Dictionary<DateTime, double>();
            foreach (var kv in _data.All())
                snap[kv.Key] = kv.Value;
            if (_segStart.HasValue)
            {
                var running = Math.Max(0, (DateTime.Now - _segStart.Value).TotalSeconds);
                snap[DateTime.Today] = _data.Get(DateTime.Today) + running;
            }

            // One history window: reload and raise an open one instead of
            // stacking another copy.
            var hf = OpenHistory();
            if (hf != null)
            {
                hf.Reload(snap, _appTracker, _colors, _settings);
                if (hf.WindowState == FormWindowState.Minimized)
                    hf.WindowState = FormWindowState.Normal;
                hf.Activate();
                return;
            }
            hf = new HistoryForm(snap, _appTracker, _colors, _settings);
            hf.RequestColors += OnFarben;
            hf.Show();
            hf.Activate();
        }

        void OnExit(object sender, EventArgs e)
        {
            if (_exiting) return;
            _exiting = true;

            CommitSegment();
            _timer.Stop();
            _appTimer.Stop();
            _updateTimer.Stop();
            SystemEvents.SessionSwitch  -= OnSessionSwitch;
            SystemEvents.SessionEnding  -= OnSessionEnding;
            _tray.Visible = false;
            _tray.Dispose();
            if (_overlay != null)
            {
                _overlay.AllowClose();
                _overlay.SavePositionPublic();
                _overlay.Dispose();
                _overlay = null;
            }
            if (_anchor != null)
            {
                _anchor.AllowClose();
                _anchor.Dispose();
                _anchor = null;
            }
            DestroyOwnedIcon(_currentIcon);
            _currentIcon = null;
            if (_iconBitmap != null) _iconBitmap.Dispose();
        }
    }

    // ── Floating overlay window ───────────────────────────────────────────────
    class OverlayForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOMOVE    = 0x0002;
        const uint SWP_NOSIZE    = 0x0001;
        const uint SWP_NOACTIVATE = 0x0010;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public event EventHandler RequestHistory;

        bool _dragging;
        bool _allowClose;
        Point _dragStart;
        System.Windows.Forms.Timer _topmostTimer;
        ColorScheme _colors;

        public void AllowClose() { _allowClose = true; }

        public OverlayForm(ColorScheme colors)
        {
            _colors = colors;

            FormBorderStyle = FormBorderStyle.None;
            TopMost         = true;
            ShowInTaskbar   = false;
            BackColor       = _colors.OverlayBg;
            Opacity         = 0.88;
            Size            = new Size(88, 36);
            Cursor          = Cursors.SizeAll;

            Location = DefaultLocation();
            LoadPosition();
            EnsureOnScreen();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            var lbl = new Label();
            lbl.Name      = "lblTime";
            lbl.Text      = "00:00";
            lbl.Dock      = DockStyle.Fill;
            lbl.ForeColor = _colors.AccentGreen;
            lbl.Font      = new Font("Consolas", 15f, FontStyle.Bold);
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.Cursor    = Cursors.SizeAll;
            lbl.MouseDown   += OnMouseDown;
            lbl.MouseMove   += OnMouseMove;
            lbl.MouseUp     += OnMouseUp;
            lbl.DoubleClick += OnWidgetDoubleClick;
            Controls.Add(lbl);

            MouseDown   += OnMouseDown;
            MouseMove   += OnMouseMove;
            MouseUp     += OnMouseUp;
            DoubleClick += OnWidgetDoubleClick;
            LocationChanged += OnLocationChanged;

            _topmostTimer = new System.Windows.Forms.Timer();
            _topmostTimer.Interval = 1000;
            _topmostTimer.Tick += OnTopmostTick;
            _topmostTimer.Start();
        }

        public void ApplyColors(ColorScheme colors)
        {
            _colors   = colors;
            BackColor = colors.OverlayBg;
            var lbl = Controls["lblTime"] as Label;
            if (lbl != null) lbl.ForeColor = colors.AccentGreen;
            Invalidate();
        }

        void OnTopmostTick(object sender, EventArgs e)
        {
            if (!Visible || !IsHandleCreated) return;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        Point DefaultLocation()
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            return new Point(screen.Right - Width - 8, screen.Bottom - Height - 4);
        }

        // Saved position may point at a monitor that is gone (undocked
        // laptop, changed layout): fall back to the default corner.
        void EnsureOnScreen()
        {
            var center = new Point(Left + Width / 2, Top + Height / 2);
            if (!Screen.AllScreens.Any(s => s.WorkingArea.Contains(center)))
                Location = DefaultLocation();
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(EnsureOnScreen)); return; }
            EnsureOnScreen();
        }

        protected override void Dispose(bool disposing)
        {
            // Static event: unsubscribe or the form is never collected.
            if (disposing) SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            base.Dispose(disposing);
        }

        void OnWidgetDoubleClick(object sender, EventArgs e)
        {
            if (RequestHistory != null) RequestHistory(this, EventArgs.Empty);
        }

        void OnLocationChanged(object sender, EventArgs e) { SavePositionPublic(); }

        public void SetTime(string hhmm)
        {
            if (InvokeRequired) { Invoke(new Action<string>(SetTime), hhmm); return; }
            var lbl = Controls["lblTime"] as Label;
            if (lbl != null) lbl.Text = hhmm;
        }

        void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = e.Location;
            }
            else if (e.Button == MouseButtons.Right && ContextMenuStrip != null)
            {
                // Show the same menu as the tray icon.
                // Convert to screen coords because the sender may be the child label.
                var src = sender as Control;
                var screenPt = (src != null)
                    ? src.PointToScreen(e.Location)
                    : PointToScreen(e.Location);
                ContextMenuStrip.Show(screenPt);
            }
        }
        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
                Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
        }
        void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = false; SavePositionPublic(); }
        }

        static string PosFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LoginTimer", "overlay.pos");

        public void SavePositionPublic()
        {
            try { File.WriteAllText(PosFile, Location.X + "," + Location.Y); } catch { }
        }

        void LoadPosition()
        {
            try
            {
                if (!File.Exists(PosFile)) return;
                var parts = File.ReadAllText(PosFile).Split(',');
                if (parts.Length == 2)
                {
                    int x, y;
                    if (int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y))
                        Location = new Point(x, y);
                }
            }
            catch { }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _topmostTimer.Start(); else _topmostTimer.Stop();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_allowClose)
            {
                // OnExit already ran — let the form close normally.
                _topmostTimer.Stop();
                base.OnFormClosing(e);
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // User clicked X on the overlay: hide, keep running.
                e.Cancel = true;
                Hide();
            }
            else if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                // Windows shutdown/restart: data already committed via SessionEnding.
                // Do NOT cancel — blocking WM_QUERYENDSESSION prevents restart/shutdown.
                _topmostTimer.Stop();
            }
            else
            {
                // Installer (WixCloseApplications) or Task Manager: cancel the
                // individual form close and request Application.Exit() so that
                // TrayApp.OnExit() saves data before the process terminates.
                e.Cancel = true;
                _topmostTimer.Stop();
                Application.Exit();
            }
        }
    }

    // ── Hidden anchor window ─────────────────────────────────────────────────
    // Gives Restart Manager and WixCloseApplications a named, non-toolwindow
    // handle to identify this process and send graceful WM_CLOSE signals.
    // Visually invisible: 1×1 black pixel, TransparencyKey = black, no taskbar.
    class AnchorForm : Form
    {
        bool _allowClose;
        public void AllowClose() { _allowClose = true; }

        public AnchorForm()
        {
            Text            = "LoginTimer";
            ShowInTaskbar   = false;
            FormBorderStyle = FormBorderStyle.None;
            Size            = new Size(1, 1);
            BackColor       = Color.Black;
            TransparencyKey = Color.Black; // fully transparent, click-through
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_allowClose) { base.OnFormClosing(e); return; }
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true; // shouldn't happen, but guard it
            else if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                // Windows shutdown/restart: data already committed via SessionEnding.
                // Do NOT cancel — blocking WM_QUERYENDSESSION prevents restart/shutdown.
            }
            else
            {
                // Installer / Task Manager → clean exit.
                e.Cancel = true;
                Application.Exit();
            }
        }
    }

    // ── History window ────────────────────────────────────────────────────────
    class HistoryForm : Form
    {
        Dictionary<DateTime, double> _snap;
        AppTracker                   _apps;
        ColorScheme                  _colors;
        AppSettings                  _settings;
        static readonly string[] DE_DAYS = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

        public event EventHandler RequestColors;

        public HistoryForm(Dictionary<DateTime, double> snap,
                           AppTracker apps,
                           ColorScheme colors,
                           AppSettings settings)
        {
            Text          = "LoginTimer - Verlauf";
            Size          = new Size(760, 580);
            MinimumSize   = new Size(560, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9f);
            Reload(snap, apps, colors, settings);
        }

        // Rebuilds all tabs with fresh data and colors; keeps window size,
        // position and the selected tab.
        public void Reload(Dictionary<DateTime, double> snap, AppTracker apps,
                           ColorScheme colors, AppSettings settings)
        {
            _snap     = snap;
            _apps     = apps;
            _colors   = colors;
            _settings = settings;

            var oldTabs  = Controls.OfType<TabControl>().FirstOrDefault();
            int selected = oldTabs != null ? oldTabs.SelectedIndex : 0;
            foreach (var c in Controls.Cast<Control>().ToList()) c.Dispose();

            SuspendLayout();
            BuildUI();
            ResumeLayout();
            var tabs = Controls.OfType<TabControl>().First();
            if (selected > 0 && selected < tabs.TabCount) tabs.SelectedIndex = selected;
        }

        void BuildUI()
        {
            BackColor   = _colors.HistoryBg;
            ForeColor   = _colors.FormText;

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(DaysTab());
            tabs.TabPages.Add(WeeksTab());
            tabs.TabPages.Add(MonthsTab());
            tabs.TabPages.Add(AppsTab());
            Controls.Add(tabs);

            // Bottom button bar
            var btnPanel = new Panel();
            btnPanel.Dock   = DockStyle.Bottom;
            btnPanel.Height = 38;
            btnPanel.BackColor = _colors.HistoryBg;

            var btnColors = new Button();
            btnColors.Text   = "Farben…";
            btnColors.Height = 26;
            btnColors.Width  = 90;
            btnColors.Left   = 8;
            btnColors.Top    = 6;
            btnColors.FlatStyle = FlatStyle.Flat;
            btnColors.BackColor = _colors.HeaderBg;
            btnColors.ForeColor = _colors.FormText;
            btnColors.Click += (s, e) => { if (RequestColors != null) RequestColors(this, EventArgs.Empty); };
            btnPanel.Controls.Add(btnColors);

            var btnExport = new Button();
            btnExport.Text   = "Exportieren…";
            btnExport.Height = 26;
            btnExport.Width  = 110;
            btnExport.Left   = 106;
            btnExport.Top    = 6;
            btnExport.FlatStyle = FlatStyle.Flat;
            btnExport.BackColor = _colors.HeaderBg;
            btnExport.ForeColor = _colors.FormText;
            btnExport.Click += OnExport;
            btnPanel.Controls.Add(btnExport);

            Controls.Add(btnPanel);
            tabs.BringToFront();
        }

        DataGridView MakeGrid(string[] headers, int[] widths)
        {
            var grid = new DataGridView();
            grid.Dock                 = DockStyle.Fill;
            grid.ReadOnly             = true;
            grid.AllowUserToAddRows   = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible    = false;
            grid.SelectionMode        = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor      = _colors.GridBg;
            grid.GridColor            = _colors.GridLines;
            grid.BorderStyle          = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.AutoSizeColumnsMode  = DataGridViewAutoSizeColumnsMode.None;
            grid.ColumnHeadersHeight  = 30;
            grid.RowTemplate.Height   = 26;

            var cell = new DataGridViewCellStyle();
            cell.BackColor          = _colors.GridBg;
            cell.ForeColor          = _colors.CellText;
            cell.Font               = new Font("Consolas", 9.5f);
            cell.SelectionBackColor = _colors.SelectionBg;
            cell.SelectionForeColor = Color.White;
            cell.Alignment          = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle   = cell;

            var hdr = new DataGridViewCellStyle();
            hdr.BackColor  = _colors.HeaderBg;
            hdr.ForeColor  = _colors.HeaderText;
            hdr.Font       = new Font("Segoe UI", 9f, FontStyle.Bold);
            hdr.Alignment  = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle = hdr;

            for (int i = 0; i < headers.Length; i++)
            {
                var col = new DataGridViewTextBoxColumn();
                col.HeaderText = headers[i];
                col.Width      = widths[i];
                col.SortMode   = DataGridViewColumnSortMode.NotSortable;
                grid.Columns.Add(col);
            }
            return grid;
        }

        // Helper: header bar above a grid
        Panel MakeBar(string text)
        {
            var bar = new Panel();
            bar.Dock      = DockStyle.Top;
            bar.Height    = 40;
            bar.BackColor = _colors.GridBg;
            var lbl = new Label();
            lbl.Text      = text;
            lbl.Dock      = DockStyle.Fill;
            lbl.ForeColor = _colors.AccentGreen;
            lbl.Font      = new Font("Consolas", 12f, FontStyle.Bold);
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Padding   = new Padding(14, 0, 0, 0);
            bar.Controls.Add(lbl);
            return bar;
        }

        TabPage DaysTab()
        {
            var tp = new TabPage("  Tage  ");
            tp.BackColor = _colors.HistoryBg;

            var todaySec = _snap.ContainsKey(DateTime.Today) ? _snap[DateTime.Today] : 0.0;
            var bar = MakeBar("Heute:   " + FormatHM(todaySec));

            var grid = MakeGrid(
                // Compared with the previous day that has data (Mon → Fri).
                new string[] { "Datum",  "Tag", "Dauer", "Stunden", "+/- letzter Tag" },
                new int[]    { 108,       48,    108,     88,         120              });

            var sorted = _snap.OrderByDescending(kv => kv.Key).Take(60).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var kv  = sorted[i];
                int dow = (int)kv.Key.DayOfWeek;
                string day = DE_DAYS[dow == 0 ? 6 : dow - 1];

                string pct = "-";
                if (i + 1 < sorted.Count && sorted[i + 1].Value > 0)
                {
                    double change = (kv.Value - sorted[i + 1].Value) / sorted[i + 1].Value * 100.0;
                    pct = (change >= 0 ? "+" : "") + ((int)Math.Round(change)).ToString() + "%";
                }
                int row = grid.Rows.Add(
                    kv.Key.ToString("dd.MM.yyyy"), day,
                    FormatHM(kv.Value),
                    (kv.Value / 3600.0).ToString("F2", CultureInfo.InvariantCulture),
                    pct);
                if (pct != "-")
                    grid.Rows[row].Cells[4].Style.ForeColor =
                        pct.StartsWith("+") ? _colors.AccentGreen : _colors.NegativeRed;
            }

            tp.Controls.Add(grid);
            tp.Controls.Add(bar);
            return tp;
        }

        TabPage WeeksTab()
        {
            var tp = new TabPage("  Wochen  ");
            tp.BackColor = _colors.HistoryBg;

            double targetSecs = _settings.WeeklyTargetHours * 3600.0;
            bool withTarget   = targetSecs > 0;
            var headers = new List<string> { "Woche", "Tage", "Gesamt", "Ø / Tag" };
            var widths  = new List<int>    { 120,     50,     95,       95        };
            if (withTarget) { headers.Add("+/- Soll"); widths.Add(95); }
            headers.Add("+/- Vorwoche"); widths.Add(100);
            var grid = MakeGrid(headers.ToArray(), widths.ToArray());
            int pctCol = headers.Count - 1;

            var groups = _snap
                .GroupBy(kv => IsoWeekKey(kv.Key))
                .OrderByDescending(g => g.Key)
                .Take(16).ToList();

            int currentWeek = IsoWeekKey(DateTime.Today);
            double balance  = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                var g     = groups[i];
                double total = g.Sum(kv => kv.Value);
                int n     = g.Count();
                string lbl = string.Format("KW {0:D2}  {1}", g.Key % 100, g.Key / 100);

                string pct = "-";
                if (i + 1 < groups.Count)
                {
                    double prev = groups[i + 1].Sum(kv => kv.Value);
                    if (prev > 0)
                    {
                        double change = (total - prev) / prev * 100.0;
                        pct = (change >= 0 ? "+" : "") + ((int)Math.Round(change)).ToString() + "%";
                    }
                }

                var cells = new List<object> { lbl, n, FormatHM(total), FormatHM(total / n) };
                double diff = 0;
                if (withTarget)
                {
                    diff = total - WeekTarget(g.Key, currentWeek, DateTime.Today, targetSecs);
                    balance += diff;
                    cells.Add(FormatSigned(diff));
                }
                cells.Add(pct);
                int row = grid.Rows.Add(cells.ToArray());
                if (withTarget)
                    grid.Rows[row].Cells[4].Style.ForeColor =
                        diff >= 0 ? _colors.AccentGreen : _colors.NegativeRed;
                if (pct != "-")
                    grid.Rows[row].Cells[pctCol].Style.ForeColor =
                        pct.StartsWith("+") ? _colors.AccentGreen : _colors.NegativeRed;
            }

            tp.Controls.Add(grid);
            if (withTarget)
                tp.Controls.Add(MakeBar(string.Format(
                    "Saldo {0} Wochen:  {1}   (Soll {2} h)",
                    groups.Count, FormatSigned(balance),
                    _settings.WeeklyTargetHours.ToString("0.#", CultureInfo.GetCultureInfo("de-DE")))));
            return tp;
        }

        // Full target for finished weeks. The current week only counts the
        // working days Mon–Fri up to and including today (Monday = 1/5).
        internal static double WeekTarget(int weekKey, int currentWeekKey,
                                          DateTime today, double targetSecs)
        {
            if (weekKey != currentWeekKey) return targetSecs;
            int dayIndex = ((int)today.DayOfWeek + 6) % 7; // Mon = 0 … Sun = 6
            return targetSecs * Math.Min(5, dayIndex + 1) / 5.0;
        }

        static string FormatSigned(double seconds)
        {
            return (seconds < 0 ? "-" : "+") + FormatHM(Math.Abs(seconds));
        }

        TabPage MonthsTab()
        {
            var tp = new TabPage("  Monate  ");
            tp.BackColor = _colors.HistoryBg;

            var grid = MakeGrid(
                new string[] { "Monat",  "Tage", "Gesamt", "Ø / Tag", "Ø / Woche", "+/- Vormonat" },
                new int[]    { 130,       48,     100,       100,       100,          96            });

            var groups = _snap
                .GroupBy(kv => kv.Key.Year * 100 + kv.Key.Month)
                .OrderByDescending(g => g.Key)
                .Take(12).ToList();

            var deDe = CultureInfo.GetCultureInfo("de-DE");
            for (int i = 0; i < groups.Count; i++)
            {
                var g     = groups[i];
                double total = g.Sum(kv => kv.Value);
                int n     = g.Count();
                var rep   = g.First().Key;
                string lbl = new DateTime(rep.Year, rep.Month, 1).ToString("MMMM yyyy", deDe);

                string pct = "-";
                if (i + 1 < groups.Count)
                {
                    double prev = groups[i + 1].Sum(kv => kv.Value);
                    if (prev > 0)
                    {
                        double change = (total - prev) / prev * 100.0;
                        pct = (change >= 0 ? "+" : "") + ((int)Math.Round(change)).ToString() + "%";
                    }
                }
                int row = grid.Rows.Add(lbl, n, FormatHM(total), FormatHM(total / n),
                    FormatHM(total / Math.Max(1.0, n / 5.0)), pct);
                if (pct != "-")
                    grid.Rows[row].Cells[5].Style.ForeColor =
                        pct.StartsWith("+") ? _colors.AccentGreen : _colors.NegativeRed;
            }

            tp.Controls.Add(grid);
            return tp;
        }

        TabPage AppsTab()
        {
            var tp = new TabPage("  Apps  ");
            tp.BackColor = _colors.HistoryBg;

            var bar = MakeBar("Aktivzeit pro Anwendung");
            bar.Padding = new Padding(0, 8, 10, 0);
            var range = new ComboBox();
            range.DropDownStyle = ComboBoxStyle.DropDownList;
            range.FlatStyle     = FlatStyle.Flat;
            range.BackColor     = _colors.HeaderBg;
            range.ForeColor     = _colors.FormText;
            range.Width         = 150;
            range.Dock          = DockStyle.Right;
            range.Items.AddRange(new object[]
                { "Heute", "Gestern", "Letzte 7 Tage", "Letzte 30 Tage", "Gesamt" });
            bar.Controls.Add(range);

            var grid = MakeGrid(
                new string[] { "Anwendung", "Zeit", "Stunden", "Anteil" },
                new int[]    { 240,          110,    100,       80       });

            var noData = new Label();
            noData.Text      = "Keine App-Daten im gewählten Zeitraum.\n" +
                               "Die Aufzeichnung läuft im Hintergrund (alle 10 s).";
            noData.Dock      = DockStyle.Fill;
            noData.ForeColor = _colors.PlaceholderText;
            noData.Font      = new Font("Segoe UI", 9.5f);
            noData.TextAlign = ContentAlignment.MiddleCenter;

            range.SelectedIndexChanged += delegate { FillApps(grid, noData, range.SelectedIndex); };

            tp.Controls.Add(grid);
            tp.Controls.Add(noData);
            tp.Controls.Add(bar);
            range.SelectedIndex = 0; // fills the grid
            return tp;
        }

        void FillApps(DataGridView grid, Label noData, int rangeIndex)
        {
            var today = DateTime.Today;
            DateTime from = today, to = today;
            switch (rangeIndex)
            {
                case 1: from = to = today.AddDays(-1); break;
                case 2: from = today.AddDays(-6);      break;
                case 3: from = today.AddDays(-29);     break;
                case 4: from = DateTime.MinValue;      break;
            }

            // Several process names can share one display name (teams / ms-teams).
            var byName = _apps.GetRange(from, to)
                .GroupBy(kv => WindowHelper.GetFriendlyName(kv.Key))
                .Select(g => new KeyValuePair<string, double>(g.Key, g.Sum(kv => kv.Value)))
                .OrderByDescending(kv => kv.Value)
                .ToList();
            double sum = byName.Sum(kv => kv.Value);

            grid.Rows.Clear();
            foreach (var kv in byName)
                grid.Rows.Add(
                    kv.Key,
                    FormatHM(kv.Value),
                    (kv.Value / 3600.0).ToString("F2", CultureInfo.InvariantCulture),
                    sum > 0 ? (kv.Value / sum * 100.0).ToString("0") + " %" : "-");
            grid.Visible   = byName.Count > 0;
            noData.Visible = byName.Count == 0;
        }

        void OnExport(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter   = "CSV-Datei (*.csv)|*.csv";
                dlg.FileName = "LoginTimer-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, BuildCsv(_snap), new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export fehlgeschlagen: " + ex.Message, "LoginTimer",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // One row per day, oldest first. Semicolons and decimal comma so the
        // file opens directly in a German Excel; UTF-8 BOM for the umlauts.
        internal static string BuildCsv(Dictionary<DateTime, double> snap)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var sb = new StringBuilder("Datum;Wochentag;Dauer;Stunden\r\n");
            foreach (var kv in snap.OrderBy(k => k.Key))
            {
                int dow = (int)kv.Key.DayOfWeek;
                sb.Append(kv.Key.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).Append(';')
                  .Append(DE_DAYS[dow == 0 ? 6 : dow - 1]).Append(';')
                  .Append(FormatHM(kv.Value)).Append(';')
                  .Append((kv.Value / 3600.0).ToString("F2", de)).Append("\r\n");
            }
            return sb.ToString();
        }

        // ISO 8601 week as yyyyww. Calendar.GetWeekOfYear is not ISO-conform
        // (Dec 29-31 can get week 53 instead of week 1), and the week-based year
        // differs from the calendar year around New Year. ISO rule: a week
        // belongs to the year that contains its Thursday.
        internal static int IsoWeekKey(DateTime d)
        {
            int daysSinceMonday = ((int)d.DayOfWeek + 6) % 7;
            var thursday = d.Date.AddDays(3 - daysSinceMonday);
            return thursday.Year * 100 + (thursday.DayOfYear - 1) / 7 + 1;
        }

        static string FormatHM(double seconds)
        {
            int h = (int)seconds / 3600;
            int m = ((int)seconds % 3600) / 60;
            return string.Format("{0:D2}:{1:D2}", h, m);
        }
    }

    // ── Settings dialog ───────────────────────────────────────────────────────
    class SettingsForm : Form
    {
        readonly NumericUpDown _idle;
        readonly NumericUpDown _target;

        public int    IdleMinutes       { get { return (int)_idle.Value; } }
        public double WeeklyTargetHours { get { return (double)_target.Value; } }

        public SettingsForm(AppSettings s)
        {
            Text            = "Einstellungen";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            TopMost         = true;
            StartPosition   = FormStartPosition.CenterScreen;
            ClientSize      = new Size(440, 216);
            Font            = new Font("Segoe UI", 9f);
            BackColor       = Color.FromArgb(32, 32, 32);
            ForeColor       = Color.FromArgb(200, 200, 200);

            _idle = AddRow(16, "Pause bei Inaktivität nach (Minuten):",
                "0 = aus. Ohne Maus- oder Tastatureingabe wird die Zeit angehalten\n" +
                "(auch beim Lesen oder Video schauen).",
                MakeNumber(0, 240, 0, s.IdleMinutes));
            _target = AddRow(100, "Wochen-Soll (Stunden):",
                "0 = aus. Zeigt \"+/- Soll\" und den Saldo im Tab \"Wochen\".",
                MakeNumber(0, 80, 1, (decimal)s.WeeklyTargetHours));

            var btnOk = MakeButton("OK", ClientSize.Width - 176, Color.FromArgb(55, 100, 70));
            btnOk.ForeColor    = Color.White;
            btnOk.DialogResult = DialogResult.OK;
            AcceptButton       = btnOk;
            var btnCancel = MakeButton("Abbrechen", ClientSize.Width - 94, Color.FromArgb(55, 55, 55));
            btnCancel.DialogResult = DialogResult.Cancel;
            CancelButton           = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }

        static NumericUpDown MakeNumber(decimal min, decimal max, int decimals, decimal value)
        {
            var num = new NumericUpDown();
            num.Minimum       = min;
            num.Maximum       = max;
            num.DecimalPlaces = decimals;
            num.Increment     = decimals > 0 ? 0.5m : 1m;
            num.Value         = Math.Min(max, Math.Max(min, value));
            num.Width         = 80;
            num.TextAlign     = HorizontalAlignment.Right;
            num.BackColor     = Color.FromArgb(55, 55, 55);
            num.ForeColor     = Color.FromArgb(220, 220, 220);
            return num;
        }

        NumericUpDown AddRow(int top, string label, string hint, NumericUpDown num)
        {
            var lbl = new Label();
            lbl.Text     = label;
            lbl.Location = new Point(12, top + 3);
            lbl.AutoSize = true;
            Controls.Add(lbl);

            num.Location = new Point(ClientSize.Width - 92, top);
            Controls.Add(num);

            var h = new Label();
            h.Text      = hint;
            h.Location  = new Point(12, top + 30);
            h.AutoSize  = true;
            h.ForeColor = Color.FromArgb(140, 140, 140);
            Controls.Add(h);
            return num;
        }

        Button MakeButton(string text, int left, Color back)
        {
            var b = new Button();
            b.Text      = text;
            b.Width     = 82;
            b.Height    = 28;
            b.Left      = left;
            b.Top       = ClientSize.Height - 40;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = Color.FromArgb(200, 200, 200);
            return b;
        }
    }

    // ── Color settings dialog ─────────────────────────────────────────────────
    class ColorSettingsForm : Form
    {
        struct Slot
        {
            public string Label;
            public Func<ColorScheme, Color> Get;
            public Action<ColorScheme, Color> Set;
            public Panel Swatch;
        }

        ColorScheme _working;
        List<Slot>  _slots;

        public ColorScheme Result { get { return _working; } }

        public ColorSettingsForm(ColorScheme current)
        {
            _working = Copy(current);
            BuildUI();
        }

        static ColorScheme Copy(ColorScheme src)
        {
            return new ColorScheme
            {
                IconBackground  = src.IconBackground,
                AccentGreen     = src.AccentGreen,
                OverlayBg       = src.OverlayBg,
                HistoryBg       = src.HistoryBg,
                GridBg          = src.GridBg,
                GridLines       = src.GridLines,
                HeaderBg        = src.HeaderBg,
                CellText        = src.CellText,
                HeaderText      = src.HeaderText,
                FormText        = src.FormText,
                PlaceholderText = src.PlaceholderText,
                SelectionBg     = src.SelectionBg,
                NegativeRed     = src.NegativeRed,
            };
        }

        void BuildUI()
        {
            Text            = "Farben";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            TopMost         = true;
            ShowInTaskbar   = false;
            ClientSize      = new Size(420, 510);
            Font            = new Font("Segoe UI", 9f);
            BackColor       = Color.FromArgb(32, 32, 32);
            ForeColor       = Color.FromArgb(200, 200, 200);

            _slots = new List<Slot>
            {
                MakeSlot("Icon-Hintergrund",       c => c.IconBackground,  (c,v) => c.IconBackground  = v),
                MakeSlot("Akzentfarbe",            c => c.AccentGreen,     (c,v) => c.AccentGreen     = v),
                MakeSlot("Overlay-Hintergrund",    c => c.OverlayBg,       (c,v) => c.OverlayBg       = v),
                MakeSlot("Verlauf-Hintergrund",    c => c.HistoryBg,       (c,v) => c.HistoryBg       = v),
                MakeSlot("Tabellen-Hintergrund",   c => c.GridBg,          (c,v) => c.GridBg          = v),
                MakeSlot("Tabellenlinien",         c => c.GridLines,       (c,v) => c.GridLines       = v),
                MakeSlot("Kopfzeilen-Hintergrund", c => c.HeaderBg,        (c,v) => c.HeaderBg        = v),
                MakeSlot("Zellentext",             c => c.CellText,        (c,v) => c.CellText        = v),
                MakeSlot("Kopfzeilentext",         c => c.HeaderText,      (c,v) => c.HeaderText      = v),
                MakeSlot("Formulartext",           c => c.FormText,        (c,v) => c.FormText        = v),
                MakeSlot("Platzhaltertext",        c => c.PlaceholderText, (c,v) => c.PlaceholderText = v),
                MakeSlot("Auswahl-Hintergrund",    c => c.SelectionBg,     (c,v) => c.SelectionBg     = v),
                MakeSlot("Negativwert (Rot)",      c => c.NegativeRed,     (c,v) => c.NegativeRed     = v),
            };

            var tbl = new TableLayoutPanel();
            tbl.ColumnCount = 3;
            tbl.RowCount    = _slots.Count;
            tbl.Dock        = DockStyle.None;
            tbl.Left        = 12;
            tbl.Top         = 12;
            tbl.Width       = 396;
            tbl.Height      = _slots.Count * 34;
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i]; // capture for lambda
                int idx  = i;
                tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

                var swatch = new Panel();
                swatch.Size        = new Size(24, 24);
                swatch.BackColor   = slot.Get(_working);
                swatch.BorderStyle = BorderStyle.FixedSingle;
                swatch.Margin      = new Padding(2, 4, 4, 4);
                slot.Swatch = swatch;
                _slots[idx] = slot;
                tbl.Controls.Add(swatch, 0, i);

                var lbl = new Label();
                lbl.Text      = slot.Label;
                lbl.Dock      = DockStyle.Fill;
                lbl.TextAlign = ContentAlignment.MiddleLeft;
                lbl.ForeColor = Color.FromArgb(200, 200, 200);
                tbl.Controls.Add(lbl, 1, i);

                var btn = new Button();
                btn.Text      = "Ändern…";
                btn.Height    = 26;
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = Color.FromArgb(55, 55, 55);
                btn.ForeColor = Color.FromArgb(200, 200, 200);
                btn.Margin    = new Padding(2, 3, 2, 3);
                var capturedIdx = idx;
                btn.Click += (s, e) => OnChange(capturedIdx);
                tbl.Controls.Add(btn, 2, i);
            }

            Controls.Add(tbl);

            // Bottom buttons
            int bottomY = tbl.Bottom + 14;

            var btnReset = new Button();
            btnReset.Text      = "Standard wiederherstellen";
            btnReset.Width     = 190;
            btnReset.Height    = 28;
            btnReset.Left      = 12;
            btnReset.Top       = bottomY;
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.BackColor = Color.FromArgb(55, 55, 55);
            btnReset.ForeColor = Color.FromArgb(200, 200, 200);
            btnReset.Click    += OnReset;
            Controls.Add(btnReset);

            var btnOk = new Button();
            btnOk.Text         = "OK";
            btnOk.Width        = 75;
            btnOk.Height       = 28;
            btnOk.Left         = ClientSize.Width - 170;
            btnOk.Top          = bottomY;
            btnOk.FlatStyle    = FlatStyle.Flat;
            btnOk.BackColor    = Color.FromArgb(55, 100, 70);
            btnOk.ForeColor    = Color.White;
            btnOk.DialogResult = DialogResult.OK;
            AcceptButton       = btnOk;
            Controls.Add(btnOk);

            var btnCancel = new Button();
            btnCancel.Text         = "Abbrechen";
            btnCancel.Width        = 85;
            btnCancel.Height       = 28;
            btnCancel.Left         = ClientSize.Width - 88;
            btnCancel.Top          = bottomY;
            btnCancel.FlatStyle    = FlatStyle.Flat;
            btnCancel.BackColor    = Color.FromArgb(55, 55, 55);
            btnCancel.ForeColor    = Color.FromArgb(200, 200, 200);
            btnCancel.DialogResult = DialogResult.Cancel;
            CancelButton           = btnCancel;
            Controls.Add(btnCancel);

            ClientSize = new Size(420, bottomY + 44);
        }

        Slot MakeSlot(string label,
                      Func<ColorScheme, Color> get,
                      Action<ColorScheme, Color> set)
        {
            return new Slot { Label = label, Get = get, Set = set };
        }

        void OnChange(int idx)
        {
            var slot = _slots[idx];
            using (var dlg = new ColorDialog())
            {
                dlg.Color    = slot.Get(_working);
                dlg.FullOpen = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                slot.Set(_working, dlg.Color);
                slot.Swatch.BackColor = dlg.Color;
            }
        }

        void OnReset(object sender, EventArgs e)
        {
            _working = ColorScheme.Default();
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.Swatch.BackColor = slot.Get(_working);
            }
        }
    }
}
