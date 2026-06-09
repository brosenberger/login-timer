using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

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

        const uint MONITOR_DEFAULTTONULL = 0;
        const uint GW_HWNDNEXT           = 2;
        const int  SW_SHOWMINIMIZED      = 2;

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
                    if (wp.showCmd != SW_SHOWMINIMIZED)
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
                    if (_systemProcs.Contains(procName)) continue;

                    result.Add(procName.ToLower());
                    break; // topmost app found for this monitor
                }
            }
            return new List<string>(result);
        }

        static string GetProcessNameSafe(int pid)
        {
            try { using (var p = Process.GetProcessById(pid)) return p.ProcessName; }
            catch { return null; }
        }
    }

    // ── Per-app time storage ──────────────────────────────────────────────────
    class AppTracker
    {
        readonly string _path;
        // date → processName → accumulated seconds
        Dictionary<DateTime, Dictionary<string, double>> _data;

        public AppTracker(string path) { _path = path; Load(); }

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

        public Dictionary<string, double> GetDay(DateTime day)
        {
            Dictionary<string, double> d;
            if (_data.TryGetValue(day.Date, out d))
                return new Dictionary<string, double>(d, StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var lines = new List<string>();
                foreach (var day in _data.OrderBy(kv => kv.Key))
                    foreach (var app in day.Value.OrderByDescending(kv => kv.Value))
                        lines.Add(day.Key.ToString("yyyy-MM-dd") + "," +
                                   app.Key + "," +
                                   app.Value.ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(_path, lines.ToArray());
            }
            catch { }
        }

        void Load()
        {
            _data = new Dictionary<DateTime, Dictionary<string, double>>();
            if (!File.Exists(_path)) return;
            foreach (var line in File.ReadAllLines(_path))
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
                if (!_data.ContainsKey(d.Date))
                    _data[d.Date] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _data[d.Date][name] = s;
            }
        }
    }

    // ── Entry point ───────────────────────────────────────────────────────────
    static class Program
    {
        static Mutex _mutex = new Mutex(true, "LoginTimerMutex_v1");

        static readonly string ErrorLog =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LoginTimer", "error.log");

        [STAThread]
        static void Main()
        {
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            if (!_mutex.WaitOne(0, true))
            {
                MessageBox.Show("LoginTimer laeuft bereits in der Taskleiste.",
                    "LoginTimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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

        static void LogError(string msg)
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

    // ── Storage: simple CSV  date,seconds ─────────────────────────────────────
    class DayData
    {
        readonly Dictionary<DateTime, double> _secs = new Dictionary<DateTime, double>();
        readonly string _path;

        public DayData(string path)
        {
            _path = path;
            Load();
        }

        void Load()
        {
            if (!File.Exists(_path)) return;
            foreach (var line in File.ReadAllLines(_path))
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                DateTime d;
                double s;
                if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                if (!double.TryParse(parts[1].Trim(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out s)) continue;
                _secs[d.Date] = s;
            }
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var lines = _secs
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Key.ToString("yyyy-MM-dd") + "," +
                              kv.Value.ToString(CultureInfo.InvariantCulture));
            File.WriteAllLines(_path, lines.ToArray());
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
        DayData    _data;
        AppTracker _appTracker;
        DateTime?  _segStart;
        NotifyIcon _tray;
        System.Windows.Forms.Timer _timer;       // 30 s: checkpoint + icon update
        System.Windows.Forms.Timer _appTimer;    // 10 s: per-monitor app tracking
        Icon   _currentIcon;
        Bitmap _iconBitmap;  // must stay alive while icon handle is in use
        OverlayForm _overlay;
        System.Threading.SynchronizationContext _syncCtx; // marshal back to UI thread

        public TrayApp()
        {
            _dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LoginTimer", "data.csv");

            _data = new DayData(_dataPath);

            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LoginTimer", "apps.csv");
            _appTracker = new AppTracker(appDataPath);

            _segStart = DateTime.Now;
            // _syncCtx is captured lazily on the first timer tick (see OnAppTick),
            // because SynchronizationContext.Current is null here — Application.Run
            // installs the WinForms context only after the constructor returns.

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = BuildMenu();
            _tray.DoubleClick += OnDoubleClick;
            _tray.Text = "LoginTimer";

            UpdateIcon();
            _tray.Visible = true;

            _overlay = new OverlayForm();
            _overlay.ContextMenuStrip = _tray.ContextMenuStrip;
            _overlay.RequestHistory += delegate { ShowHistory(); };
            _overlay.Show();
            _overlay.SetTime(FormatHM(TodaySeconds())); // show correct time immediately

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 30000;
            _timer.Tick += OnTick;
            _timer.Start();

            _appTimer = new System.Windows.Forms.Timer();
            _appTimer.Interval = 10000;
            _appTimer.Tick += OnAppTick;
            _appTimer.Start();

            SystemEvents.SessionSwitch += OnSessionSwitch;
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
            if (_segStart == null) return; // locked / disconnected

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

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock ||
                e.Reason == SessionSwitchReason.SessionLogoff ||
                e.Reason == SessionSwitchReason.RemoteDisconnect)
            {
                CommitSegment();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock ||
                     e.Reason == SessionSwitchReason.SessionLogon ||
                     e.Reason == SessionSwitchReason.RemoteConnect)
            {
                if (_segStart == null)
                    _segStart = DateTime.Now;
            }
            UpdateIcon();
        }

        // Autosave: commit elapsed, reset segment start to avoid double-count
        void Checkpoint()
        {
            if (_segStart == null) return;
            var now = DateTime.Now;

            if (_segStart.Value.Date < now.Date)
            {
                var midnight = now.Date;
                _data.Set(_segStart.Value.Date,
                    _data.Get(_segStart.Value.Date) + (midnight - _segStart.Value).TotalSeconds);
                _segStart = midnight;
            }

            var elapsed = (now - _segStart.Value).TotalSeconds;
            _data.Set(now.Date, _data.Get(now.Date) + elapsed);
            _data.Save();
            _appTracker.Save();
            _segStart = now;
        }

        // Final commit on lock / exit
        void CommitSegment()
        {
            if (_segStart == null) return;
            var now = DateTime.Now;

            if (_segStart.Value.Date < now.Date)
            {
                var midnight = now.Date;
                _data.Set(_segStart.Value.Date,
                    _data.Get(_segStart.Value.Date) + (midnight - _segStart.Value).TotalSeconds);
                _segStart = midnight;
            }

            _data.Set(now.Date,
                _data.Get(now.Date) + (now - _segStart.Value).TotalSeconds);
            _data.Save();
            _appTracker.Save();
            _segStart = null;
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
            var secs  = TodaySeconds();
            var label = FormatHM(secs);
            _tray.Text = "LoginTimer  " + label + " heute";

            var oldIcon = _currentIcon;
            _currentIcon = MakeIcon(label);
            _tray.Icon = _currentIcon;
            if (oldIcon != null) oldIcon.Dispose();

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
                g.Clear(Color.FromArgb(22, 22, 22));
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                var parts = label.Split(':');
                var green = new SolidBrush(Color.FromArgb(0, 215, 85));
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
            return Icon.FromHandle(bmp.GetHicon());
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Verlauf anzeigen", null, OnShowHistory);
            var overlayItem = new ToolStripMenuItem("Zeitanzeige (Widget)");
            overlayItem.Checked = true;
            overlayItem.Click  += OnToggleOverlay;
            m.Items.Add(overlayItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Beenden", null, OnQuit);
            return m;
        }

        void OnShowHistory(object sender, EventArgs e) { ShowHistory(); }

        void OnToggleOverlay(object sender, EventArgs e)
        {
            if (_overlay.Visible) _overlay.Hide(); else _overlay.Show();
            foreach (ToolStripItem item in _tray.ContextMenuStrip.Items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null && mi.Text.StartsWith("Zeitanzeige"))
                    mi.Checked = _overlay.Visible;
            }
        }

        void OnQuit(object sender, EventArgs e) { Application.Exit(); }

        void ShowHistory()
        {
            var snap = new Dictionary<DateTime, double>();
            foreach (var kv in _data.All())
                snap[kv.Key] = kv.Value;
            if (_segStart.HasValue)
            {
                var running = Math.Max(0, (DateTime.Now - _segStart.Value).TotalSeconds);
                snap[DateTime.Today] = _data.Get(DateTime.Today) + running;
            }

            var appSnap = _appTracker.GetDay(DateTime.Today);
            new HistoryForm(snap, appSnap).Show();
        }

        void OnExit(object sender, EventArgs e)
        {
            CommitSegment();
            _timer.Stop();
            _appTimer.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _tray.Visible = false;
            _tray.Dispose();
            if (_overlay != null) { _overlay.SavePositionPublic(); _overlay.Dispose(); }
            if (_currentIcon != null) _currentIcon.Dispose();
            if (_iconBitmap  != null) _iconBitmap.Dispose();
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
        Point _dragStart;
        System.Windows.Forms.Timer _topmostTimer;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost         = true;
            ShowInTaskbar   = false;
            BackColor       = Color.FromArgb(18, 18, 18);
            Opacity         = 0.88;
            Size            = new Size(88, 36);
            Cursor          = Cursors.SizeAll;

            var screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(screen.Right - Width - 8, screen.Bottom - Height - 4);
            LoadPosition();

            var lbl = new Label();
            lbl.Name      = "lblTime";
            lbl.Text      = "00:00";
            lbl.Dock      = DockStyle.Fill;
            lbl.ForeColor = Color.FromArgb(0, 215, 85);
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

        void OnTopmostTick(object sender, EventArgs e)
        {
            if (!Visible || !IsHandleCreated) return;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            else { _topmostTimer.Stop(); base.OnFormClosing(e); }
        }
    }

    // ── History window ────────────────────────────────────────────────────────
    class HistoryForm : Form
    {
        readonly Dictionary<DateTime, double> _snap;
        readonly Dictionary<string, double>   _todayApps;
        static readonly string[] DE_DAYS = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

        public HistoryForm(Dictionary<DateTime, double> snap,
                           Dictionary<string, double> todayApps)
        {
            _snap      = snap;
            _todayApps = todayApps;
            BuildUI();
        }

        void BuildUI()
        {
            Text        = "LoginTimer - Verlauf";
            Size        = new Size(700, 540);
            MinimumSize = new Size(540, 380);
            TopMost     = true;
            BackColor   = Color.FromArgb(28, 28, 28);
            ForeColor   = Color.FromArgb(200, 200, 200);
            Font        = new Font("Segoe UI", 9f);

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(DaysTab());
            tabs.TabPages.Add(WeeksTab());
            tabs.TabPages.Add(MonthsTab());
            tabs.TabPages.Add(AppsTab());
            Controls.Add(tabs);
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
            grid.BackgroundColor      = Color.FromArgb(35, 35, 35);
            grid.GridColor            = Color.FromArgb(55, 55, 55);
            grid.BorderStyle          = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.AutoSizeColumnsMode  = DataGridViewAutoSizeColumnsMode.None;
            grid.ColumnHeadersHeight  = 30;
            grid.RowTemplate.Height   = 26;

            var cell = new DataGridViewCellStyle();
            cell.BackColor          = Color.FromArgb(35, 35, 35);
            cell.ForeColor          = Color.FromArgb(210, 210, 210);
            cell.Font               = new Font("Consolas", 9.5f);
            cell.SelectionBackColor = Color.FromArgb(55, 100, 70);
            cell.SelectionForeColor = Color.White;
            cell.Alignment          = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle   = cell;

            var hdr = new DataGridViewCellStyle();
            hdr.BackColor  = Color.FromArgb(45, 45, 45);
            hdr.ForeColor  = Color.FromArgb(160, 160, 160);
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
            bar.BackColor = Color.FromArgb(35, 35, 35);
            var lbl = new Label();
            lbl.Text      = text;
            lbl.Dock      = DockStyle.Fill;
            lbl.ForeColor = Color.FromArgb(0, 215, 85);
            lbl.Font      = new Font("Consolas", 12f, FontStyle.Bold);
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Padding   = new Padding(14, 0, 0, 0);
            bar.Controls.Add(lbl);
            return bar;
        }

        TabPage DaysTab()
        {
            var tp = new TabPage("  Tage  ");
            tp.BackColor = Color.FromArgb(28, 28, 28);

            var todaySec = _snap.ContainsKey(DateTime.Today) ? _snap[DateTime.Today] : 0.0;
            var bar = MakeBar("Heute:   " + FormatHM(todaySec));

            var grid = MakeGrid(
                new string[] { "Datum",  "Tag", "Dauer", "Stunden", "+/- Vortag" },
                new int[]    { 108,       48,    108,     88,         88          });

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
                        pct.StartsWith("+") ? Color.FromArgb(0, 215, 85) : Color.FromArgb(220, 80, 60);
            }

            tp.Controls.Add(grid);
            tp.Controls.Add(bar);
            return tp;
        }

        TabPage WeeksTab()
        {
            var tp = new TabPage("  Wochen  ");
            tp.BackColor = Color.FromArgb(28, 28, 28);

            var grid = MakeGrid(
                new string[] { "Woche",  "Tage", "Gesamt", "O / Tag", "+/- Vorwoche" },
                new int[]    { 130,       55,     110,       110,       100           });

            var groups = _snap
                .GroupBy(kv => IsoWeekKey(kv.Key))
                .OrderByDescending(g => g.Key)
                .Take(16).ToList();

            var cal = CultureInfo.InvariantCulture.Calendar;
            for (int i = 0; i < groups.Count; i++)
            {
                var g     = groups[i];
                double total = g.Sum(kv => kv.Value);
                int n     = g.Count();
                var rep   = g.OrderBy(kv => kv.Key).First().Key;
                int week  = cal.GetWeekOfYear(rep, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                string lbl = string.Format("KW {0:D2}  {1}", week, rep.Year);

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
                int row = grid.Rows.Add(lbl, n, FormatHM(total), FormatHM(total / n), pct);
                if (pct != "-")
                    grid.Rows[row].Cells[4].Style.ForeColor =
                        pct.StartsWith("+") ? Color.FromArgb(0, 215, 85) : Color.FromArgb(220, 80, 60);
            }

            tp.Controls.Add(grid);
            return tp;
        }

        TabPage MonthsTab()
        {
            var tp = new TabPage("  Monate  ");
            tp.BackColor = Color.FromArgb(28, 28, 28);

            var grid = MakeGrid(
                new string[] { "Monat",  "Tage", "Gesamt", "O / Tag", "O / Woche", "+/- Vormonat" },
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
                        pct.StartsWith("+") ? Color.FromArgb(0, 215, 85) : Color.FromArgb(220, 80, 60);
            }

            tp.Controls.Add(grid);
            return tp;
        }

        TabPage AppsTab()
        {
            var tp = new TabPage("  Apps  ");
            tp.BackColor = Color.FromArgb(28, 28, 28);

            var bar = MakeBar("Heute – Aktivzeit pro Anwendung");

            if (_todayApps.Count == 0)
            {
                var noData = new Label();
                noData.Text      = "Noch keine App-Daten fuer heute.\n" +
                                   "Die Aufzeichnung laeuft im Hintergrund (alle 10 s).";
                noData.Dock      = DockStyle.Fill;
                noData.ForeColor = Color.FromArgb(120, 120, 120);
                noData.Font      = new Font("Segoe UI", 9.5f);
                noData.TextAlign = ContentAlignment.MiddleCenter;
                tp.Controls.Add(noData);
                tp.Controls.Add(bar);
                return tp;
            }

            var grid = MakeGrid(
                new string[] { "Anwendung", "Zeit",  "Stunden" },
                new int[]    { 240,          120,     110      });

            foreach (var kv in _todayApps.OrderByDescending(a => a.Value))
            {
                grid.Rows.Add(
                    WindowHelper.GetFriendlyName(kv.Key),
                    FormatHM(kv.Value),
                    (kv.Value / 3600.0).ToString("F2", CultureInfo.InvariantCulture));
            }

            tp.Controls.Add(grid);
            tp.Controls.Add(bar);
            return tp;
        }

        static int IsoWeekKey(DateTime d)
        {
            var cal  = CultureInfo.InvariantCulture.Calendar;
            int week = cal.GetWeekOfYear(d, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            return d.Year * 100 + week;
        }

        static string FormatHM(double seconds)
        {
            int h = (int)seconds / 3600;
            int m = ((int)seconds % 3600) / 60;
            return string.Format("{0:D2}:{1:D2}", h, m);
        }
    }
}
