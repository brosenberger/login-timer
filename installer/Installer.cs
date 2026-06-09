using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace LoginTimerInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }

    class InstallerForm : Form
    {
        // ── Shortcut via WScript.Shell (kein extra DLL noetig) ──────────────
        static void CreateShortcut(string lnkPath, string targetPath, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object sc = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type scType = sc.GetType();
                scType.InvokeMember("TargetPath",
                    BindingFlags.SetProperty, null, sc, new object[] { targetPath });
                scType.InvokeMember("Description",
                    BindingFlags.SetProperty, null, sc, new object[] { description });
                scType.InvokeMember("Save",
                    BindingFlags.InvokeMethod, null, sc, null);
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }

        static bool IsAdmin()
        {
            var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // ── UI ───────────────────────────────────────────────────────────────
        Label _lblStatus;
        Button _btnInstall;
        CheckBox _chkDesk;
        RadioButton _rbUser;
        RadioButton _rbSystem;
        string _srcExe;

        public InstallerForm()
        {
            _srcExe = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath),
                "LoginTimer.exe");

            Text = "LoginTimer – Installer";
            Size = new Size(440, 360);
            MinimumSize = new Size(440, 360);
            MaximumSize = new Size(440, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(210, 210, 210);
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUI();

            // Quelle pruefen
            if (!File.Exists(_srcExe))
            {
                _btnInstall.Enabled = false;
                _chkDesk.Enabled = false;
                SetStatus("LoginTimer.exe nicht gefunden!\nBitte zuerst build.bat ausfuehren.", Color.FromArgb(220, 80, 60));
            }
        }

        void BuildUI()
        {
            int pad = 24;
            int y = 20;

            // Titel
            var title = new Label();
            title.Text = "LoginTimer installieren";
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(0, 215, 85);
            title.Location = new Point(pad, y);
            title.AutoSize = true;
            Controls.Add(title);
            y += 44;

            // Trennlinie
            var sep = new Panel();
            sep.Location = new Point(pad, y);
            sep.Size = new Size(Width - pad * 2, 1);
            sep.BackColor = Color.FromArgb(60, 60, 60);
            Controls.Add(sep);
            y += 14;

            // Installationsort
            var lblWhere = new Label();
            lblWhere.Text = "Installationsort:";
            lblWhere.Location = new Point(pad, y);
            lblWhere.AutoSize = true;
            Controls.Add(lblWhere);
            y += 24;

            bool admin = IsAdmin();

            _rbUser = new RadioButton();
            _rbUser.Text = "Nur fuer mich  (%LocalAppData%\\LoginTimer\\)";
            _rbUser.Location = new Point(pad + 10, y);
            _rbUser.Width = 370;
            _rbUser.Checked = true;
            _rbUser.ForeColor = Color.FromArgb(210, 210, 210);
            Controls.Add(_rbUser);
            y += 26;

            _rbSystem = new RadioButton();
            _rbSystem.Text = "Fuer alle Benutzer  (C:\\Program Files\\LoginTimer\\)";
            _rbSystem.Location = new Point(pad + 10, y);
            _rbSystem.Width = 370;
            _rbSystem.Enabled = admin;
            _rbSystem.ForeColor = admin
                ? Color.FromArgb(210, 210, 210)
                : Color.FromArgb(100, 100, 100);
            Controls.Add(_rbSystem);
            y += 10;

            if (!admin)
            {
                var lblNoAdmin = new Label();
                lblNoAdmin.Text = "  (Administrator-Rechte benoetigt — als Admin neu starten fuer diese Option)";
                lblNoAdmin.Location = new Point(pad + 10, y + 18);
                lblNoAdmin.Width = 380;
                lblNoAdmin.ForeColor = Color.FromArgb(140, 140, 140);
                lblNoAdmin.Font = new Font("Segoe UI", 8f);
                Controls.Add(lblNoAdmin);
                y += 24;
            }
            y += 28;

            // Trennlinie 2
            var sep2 = new Panel();
            sep2.Location = new Point(pad, y);
            sep2.Size = new Size(Width - pad * 2, 1);
            sep2.BackColor = Color.FromArgb(60, 60, 60);
            Controls.Add(sep2);
            y += 14;

            // Optionen
            _chkDesk = new CheckBox();
            _chkDesk.Text = "Desktop-Verkuepfung erstellen";
            _chkDesk.Location = new Point(pad, y);
            _chkDesk.Width = 300;
            _chkDesk.Checked = true;
            _chkDesk.ForeColor = Color.FromArgb(210, 210, 210);
            Controls.Add(_chkDesk);
            y += 30;

            // Status
            _lblStatus = new Label();
            _lblStatus.Location = new Point(pad, y);
            _lblStatus.Size = new Size(Width - pad * 2, 40);
            _lblStatus.Text = "";
            _lblStatus.ForeColor = Color.FromArgb(180, 180, 180);
            Controls.Add(_lblStatus);
            y += 48;

            // Buttons
            _btnInstall = new Button();
            _btnInstall.Text = "Installieren && Autostart einrichten";
            _btnInstall.Location = new Point(pad, y);
            _btnInstall.Size = new Size(240, 34);
            _btnInstall.BackColor = Color.FromArgb(0, 150, 60);
            _btnInstall.ForeColor = Color.White;
            _btnInstall.FlatStyle = FlatStyle.Flat;
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnInstall.Click += OnInstall;
            Controls.Add(_btnInstall);

            var btnCancel = new Button();
            btnCancel.Text = "Abbrechen";
            btnCancel.Location = new Point(pad + 250, y);
            btnCancel.Size = new Size(100, 34);
            btnCancel.BackColor = Color.FromArgb(60, 60, 60);
            btnCancel.ForeColor = Color.FromArgb(210, 210, 210);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnCancel);
        }

        void SetStatus(string msg, Color color)
        {
            _lblStatus.Text = msg;
            _lblStatus.ForeColor = color;
        }

        void OnInstall(object sender, EventArgs e)
        {
            _btnInstall.Enabled = false;
            SetStatus("Installiere...", Color.FromArgb(200, 200, 100));

            try
            {
                bool systemWide = _rbSystem.Checked;

                // Zielordner bestimmen
                string destDir = systemWide
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LoginTimer")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoginTimer");

                Directory.CreateDirectory(destDir);
                string destExe = Path.Combine(destDir, "LoginTimer.exe");

                // Laufende Instanz beenden
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName("LoginTimer"))
                {
                    try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                }

                // EXE kopieren
                File.Copy(_srcExe, destExe, overwrite: true);

                // Autostart-Verkuepfung erstellen
                string autostartDir = Path.Combine(
                    Environment.GetFolderPath(
                        systemWide
                            ? Environment.SpecialFolder.CommonStartup
                            : Environment.SpecialFolder.Startup));
                string lnkAutostart = Path.Combine(autostartDir, "LoginTimer.lnk");
                CreateShortcut(lnkAutostart, destExe, "LoginTimer – Eingeloggte Zeit verfolgen");

                // Desktop-Verkuepfung (optional)
                if (_chkDesk.Checked)
                {
                    string lnkDesk = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "LoginTimer.lnk");
                    CreateShortcut(lnkDesk, destExe, "LoginTimer starten");
                }

                // Starten
                Process.Start(destExe);

                SetStatus("Erfolgreich installiert!\nAutostart: " + autostartDir, Color.FromArgb(0, 215, 85));

                MessageBox.Show(
                    "LoginTimer wurde erfolgreich installiert!\n\n" +
                    "Installiert in:\n  " + destDir + "\n\n" +
                    "Autostart-Verkuepfung:\n  " + lnkAutostart + "\n\n" +
                    "LoginTimer laeuft jetzt im Hintergrund.",
                    "Installation abgeschlossen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetStatus("Fehler: " + ex.Message, Color.FromArgb(220, 80, 60));
                MessageBox.Show("Fehler bei der Installation:\n\n" + ex.Message,
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnInstall.Enabled = true;
            }
        }
    }
}
