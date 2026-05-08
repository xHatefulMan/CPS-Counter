using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Text.Json;

namespace CompteurCPS
{
    public partial class Form1 : Form
    {
        private List<DateTime> leftClicks = new List<DateTime>();
        private List<DateTime> rightClicks = new List<DateTime>();
        private System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();

        private Label labelLeft = new Label();
        private Label labelRight = new Label();
        private Label gearLabel = new Label();

        private bool numbersOnly = false;
        private bool titleBarVisible = true;
        private bool positionLocked = false;
        private bool interactionLocked = false;
        private bool alwaysOnTop = true;
        private Color colorLeft = Color.FromArgb(100, 200, 255);
        private Color colorRight = Color.FromArgb(255, 150, 100);
        private float customFontSize = -1f;
        private Point lockedPosition;

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CompteurCPS", "cps_settings.json");

        private IntPtr hookId = IntPtr.Zero;
        private NativeMethods.LowLevelMouseProc hookProc;
        private Form settingsForm = null;
        private NotifyIcon trayIcon = new NotifyIcon();
        private ContextMenuStrip trayMenu = new ContextMenuStrip();

        // ── Icône embarquée ──────────────────────────────────────────────────
        private static Icon? _icon;
        private static Icon? AppIcon
        {
            get
            {
                if (_icon != null) return _icon;
                try
                {
                    var stream = System.Reflection.Assembly
                        .GetExecutingAssembly()
                        .GetManifestResourceStream("CompteurCPS.left-click.ico");
                    if (stream != null) _icon = new Icon(stream);
                }
                catch { }
                return _icon;
            }
        }

        public Form1()
        {
            try
            {
                InitializeComponent();
                LoadSettings();
                SetupMainWindow();
                SetupTray();
                SetupHook();
                SetupTimer();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  WndProc — drag zone client
        // ═════════════════════════════════════════════════════════════════════

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT && !positionLocked)
            {
                int lp = (int)(long)m.LParam;
                int sx = unchecked((short)(lp & 0xFFFF));
                int sy = unchecked((short)((lp >> 16) & 0xFFFF));
                if (!gearLabel.Bounds.Contains(this.PointToClient(new Point(sx, sy))))
                    m.Result = (IntPtr)HTCAPTION;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  SAVE / LOAD
        // ═════════════════════════════════════════════════════════════════════

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["numbersOnly"] = numbersOnly,
                    ["alwaysOnTop"] = alwaysOnTop,
                    ["colorLeftR"] = colorLeft.R,
                    ["colorLeftG"] = colorLeft.G,
                    ["colorLeftB"] = colorLeft.B,
                    ["colorRightR"] = colorRight.R,
                    ["colorRightG"] = colorRight.G,
                    ["colorRightB"] = colorRight.B,
                    ["customFontSize"] = customFontSize,
                    ["windowX"] = this.Location.X,
                    ["windowY"] = this.Location.Y,
                    ["windowW"] = this.Width,
                    ["windowH"] = this.Height
                }));
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(ConfigPath));
                if (raw == null) return;
                T Get<T>(string k, T def) => raw.TryGetValue(k, out var v) ? v.Deserialize<T>() : def;

                numbersOnly = Get("numbersOnly", false);
                alwaysOnTop = Get("alwaysOnTop", true);
                customFontSize = Get("customFontSize", -1f);
                colorLeft = Color.FromArgb(Get("colorLeftR", 100), Get("colorLeftG", 200), Get("colorLeftB", 255));
                colorRight = Color.FromArgb(Get("colorRightR", 255), Get("colorRightG", 150), Get("colorRightB", 100));
                titleBarVisible = true;
                positionLocked = false;
                interactionLocked = false;

                int wx = Get("windowX", -1), wy = Get("windowY", -1);
                int ww = Get("windowW", 340), wh = Get("windowH", 200);
                if (wx >= 0 && wy >= 0)
                {
                    this.StartPosition = FormStartPosition.Manual;
                    this.Location = new Point(wx, wy);
                }
                this.Size = new Size(Math.Max(200, ww), Math.Max(120, wh));
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  FENÊTRE PRINCIPALE
        // ═════════════════════════════════════════════════════════════════════

        private void SetupMainWindow()
        {
            this.Text = "CPS";
            this.MinimumSize = new Size(200, 120);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.TopMost = true;
            this.BackColor = Color.LimeGreen;
            this.TransparencyKey = Color.LimeGreen;
            this.DoubleBuffered = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);

            if (this.StartPosition != FormStartPosition.Manual)
            {
                this.StartPosition = FormStartPosition.Manual;
                var wa = Screen.PrimaryScreen.WorkingArea;
                this.Location = new Point(wa.Width / 4, wa.Height / 8);
                this.Size = new Size(340, 200);
            }

            if (AppIcon != null) this.Icon = AppIcon;

            labelLeft.TextAlign = ContentAlignment.MiddleCenter;
            labelLeft.BackColor = Color.LimeGreen;
            labelLeft.ForeColor = colorLeft;
            this.Controls.Add(labelLeft);

            labelRight.TextAlign = ContentAlignment.MiddleCenter;
            labelRight.BackColor = Color.LimeGreen;
            labelRight.ForeColor = colorRight;
            this.Controls.Add(labelRight);

            gearLabel.Text = "";
            gearLabel.BackColor = Color.FromArgb(38, 38, 38);
            gearLabel.Size = new Size(28, 28);
            gearLabel.Cursor = Cursors.Hand;
            gearLabel.Click += (s, e) => OpenSettings();
            gearLabel.MouseEnter += (s, e) => { gearLabel.BackColor = Color.FromArgb(62, 62, 62); gearLabel.Invalidate(); };
            gearLabel.MouseLeave += (s, e) => { gearLabel.BackColor = Color.FromArgb(38, 38, 38); gearLabel.Invalidate(); };
            gearLabel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var rc = gearLabel.ClientRectangle;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using var fnt = new Font("Segoe UI Symbol", 14f);
                using var br = new SolidBrush(Color.FromArgb(170, 170, 170));
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoClip };
                g.DrawString("⚙", fnt, br, rc, sf);
            };
            this.Controls.Add(gearLabel);

            this.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) OpenSettings(); };
            labelLeft.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) OpenSettings(); };
            labelRight.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) OpenSettings(); };

            this.ResizeEnd += (s, e) => SaveSettings();
            this.Move += (s, e) => SaveSettings();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  RECENTRER — remet tout à zéro et recentre
        // ═════════════════════════════════════════════════════════════════════

        private void RecenterWindow()
        {
            // Désactiver mode jeu et verrou avant de bouger
            interactionLocked = false;
            ApplyInteractionLock();
            positionLocked = false;

            // Recentrer
            var wa = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(
                wa.Left + (wa.Width - this.Width) / 2,
                wa.Top + (wa.Height - this.Height) / 2
            );

            // Remettre barre et premier plan
            alwaysOnTop = true;
            this.TopMost = true;
            titleBarVisible = true;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            SaveSettings();

            // Rouvrir settings pour refléter les changements
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Close();
                OpenSettings();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TRAY
        // ═════════════════════════════════════════════════════════════════════

        private void SetupTray()
        {
            trayIcon.Icon = AppIcon ?? SystemIcons.Application;
            trayIcon.Text = "CPS Counter";
            trayIcon.Visible = true;
            trayMenu.Items.Add(new ToolStripMenuItem("Parametres", null, (s, e) => OpenSettings()));
            trayMenu.Items.Add(new ToolStripMenuItem("Recentrer", null, (s, e) => RecenterWindow()));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("Quitter", null, (s, e) => Application.Exit()));
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => OpenSettings();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  SETTINGS
        // ═════════════════════════════════════════════════════════════════════

        private void OpenSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.BringToFront(); return; }

            const int SW = 520;

            settingsForm = new Form
            {
                Text = "CPS - Parametres",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(40, 40, 40),
                TopMost = true,
                StartPosition = FormStartPosition.CenterScreen
            };

            if (AppIcon != null) settingsForm.Icon = AppIcon;

            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 16, 20, 16),
                BackColor = Color.Transparent
            };
            settingsForm.Controls.Add(flow);

            const int IW = SW - 40;
            const int RH = 46;

            // Header
            var hdr = new Panel { Width = IW, Height = 52, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
            if (AppIcon != null)
                hdr.Controls.Add(new PictureBox { Image = AppIcon.ToBitmap(), Size = new Size(32, 32), Location = new Point(0, 10), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(40, 40, 40) });
            hdr.Controls.Add(new Label { Text = "CPS - Parametres", ForeColor = Color.White, Font = new Font("Segoe UI", 14, FontStyle.Bold), Location = new Point(42, 12), AutoSize = true, BackColor = Color.Transparent });
            flow.Controls.Add(hdr);

            void Sep() => flow.Controls.Add(new Panel { Width = IW, Height = 1, BackColor = Color.FromArgb(62, 62, 62), Margin = new Padding(0, 6, 0, 6) });
            void SecLbl(string t) => flow.Controls.Add(new Label { Text = t, ForeColor = Color.FromArgb(115, 115, 115), Font = new Font("Segoe UI", 7, FontStyle.Bold), Width = IW, Height = 20, Margin = new Padding(0, 2, 0, 2), BackColor = Color.Transparent });

            void MakeRow(string lbl, Control ctrl, int ctrlW = 70)
            {
                var tbl = new TableLayoutPanel { Width = IW, Height = RH, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(50, 50, 50), Margin = new Padding(0, 0, 0, 3), Padding = new Padding(12, 0, 12, 0), CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
                tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ctrlW));
                tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                tbl.Controls.Add(new Label { Text = lbl, ForeColor = Color.FromArgb(215, 215, 215), Font = new Font("Segoe UI", 10), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent }, 0, 0);
                ctrl.Anchor = AnchorStyles.None;
                tbl.Controls.Add(ctrl, 1, 0);
                flow.Controls.Add(tbl);
            }

            void MakeRowWithHelp(string lbl, Control ctrl, string tooltip)
            {
                var tbl = new TableLayoutPanel { Width = IW, Height = RH, ColumnCount = 3, RowCount = 1, BackColor = Color.FromArgb(50, 50, 50), Margin = new Padding(0, 0, 0, 3), Padding = new Padding(12, 0, 12, 0), CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
                tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
                tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
                tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                tbl.Controls.Add(new Label { Text = lbl, ForeColor = Color.FromArgb(215, 215, 215), Font = new Font("Segoe UI", 10), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent }, 0, 0);
                var h = new Label { Text = "?", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(180, 180, 180), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Help };
                new ToolTip { AutoPopDelay = 8000, InitialDelay = 300 }.SetToolTip(h, tooltip);
                tbl.Controls.Add(h, 1, 0);
                ctrl.Anchor = AnchorStyles.None;
                tbl.Controls.Add(ctrl, 2, 0);
                flow.Controls.Add(tbl);
            }

            CheckBox MakeTog(bool cur, Action<bool> cb)
            {
                var c = new CheckBox { Checked = cur, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, Text = cur ? "ON" : "OFF", ForeColor = cur ? Color.FromArgb(80, 220, 80) : Color.FromArgb(120, 120, 120), Font = new Font("Segoe UI", 9, FontStyle.Bold), Size = new Size(58, 30), BackColor = Color.FromArgb(55, 55, 55), TextAlign = ContentAlignment.MiddleCenter, TabStop = false };
                c.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
                c.FlatAppearance.CheckedBackColor = Color.FromArgb(0, 120, 0);
                c.CheckedChanged += (s, e) => { c.Text = c.Checked ? "ON" : "OFF"; c.ForeColor = c.Checked ? Color.FromArgb(80, 220, 80) : Color.FromArgb(120, 120, 120); cb(c.Checked); SaveSettings(); };
                return c;
            }

            Panel MakeSlider(int min, int max, int val, Action<int> onChange)
            {
                var p = new Panel { Size = new Size(160, RH), BackColor = Color.Transparent };
                var s = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)), TickStyle = TickStyle.None, Dock = DockStyle.Fill, BackColor = Color.FromArgb(50, 50, 50) };
                s.Scroll += (_, e) => onChange(s.Value);
                p.Controls.Add(s);
                return p;
            }

            Button MakeSwatch(Color col)
            {
                var b = new Button { BackColor = col, FlatStyle = FlatStyle.Flat, Size = new Size(58, 30), Cursor = Cursors.Hand, TabStop = false };
                b.FlatAppearance.BorderColor = Color.FromArgb(85, 85, 85);
                b.FlatAppearance.BorderSize = 1;
                return b;
            }

            // ── AFFICHAGE ─────────────────────────────────────────────────
            Sep(); SecLbl("AFFICHAGE");
            MakeRow("Taille du texte", MakeSlider(8, 80, (int)CalcFont(), v => { customFontSize = v; RefreshDisplay(); SaveSettings(); }), 170);
            MakeRow("Largeur fenetre", MakeSlider(200, 900, this.Width, v => { this.Width = v; SaveSettings(); }), 170);
            MakeRow("Hauteur fenetre", MakeSlider(120, 600, this.Height, v => { this.Height = v; SaveSettings(); }), 170);
            MakeRow("Afficher Left/Right", MakeTog(!numbersOnly, v => { numbersOnly = !v; RefreshDisplay(); }));

            // ── COULEURS ──────────────────────────────────────────────────
            Sep(); SecLbl("COULEURS");

            var swL = MakeSwatch(colorLeft);
            swL.Click += (s, e) => { using var cd = new ColorDialog { Color = colorLeft, FullOpen = true }; if (cd.ShowDialog() == DialogResult.OK) { colorLeft = cd.Color; swL.BackColor = cd.Color; labelLeft.ForeColor = cd.Color; SaveSettings(); } };
            MakeRow("Left", swL);

            var swR = MakeSwatch(colorRight);
            swR.Click += (s, e) => { using var cd = new ColorDialog { Color = colorRight, FullOpen = true }; if (cd.ShowDialog() == DialogResult.OK) { colorRight = cd.Color; swR.BackColor = cd.Color; labelRight.ForeColor = cd.Color; SaveSettings(); } };
            MakeRow("Right", swR);

            // ── OPTIONS ───────────────────────────────────────────────────
            Sep(); SecLbl("OPTIONS");
            MakeRow("Toujours au premier plan", MakeTog(alwaysOnTop, v => { alwaysOnTop = v; this.TopMost = v; }));
            MakeRow("Afficher la barre du titre", MakeTog(titleBarVisible, v => { titleBarVisible = v; ApplyTitleBar(); }));
            MakeRow("Verrouiller la position", MakeTog(positionLocked, v => { positionLocked = v; if (v) lockedPosition = this.Location; }));
            MakeRowWithHelp(
                "Mode jeu (clics passent a travers)",
                MakeTog(interactionLocked, v => { if (v && !alwaysOnTop) { alwaysOnTop = true; this.TopMost = true; } interactionLocked = v; ApplyInteractionLock(); }),
                "En mode jeu, les clics passent a travers la fenetre.\nParametres accessibles via clic droit sur l'icone CPS dans la barre des taches."
            );

            // ── BOUTONS ───────────────────────────────────────────────────
            Sep();

            // Recentrer
            var btnCenter = new Button { Text = "Recentrer la fenetre", ForeColor = Color.FromArgb(180, 180, 180), BackColor = Color.FromArgb(50, 50, 50), FlatStyle = FlatStyle.Flat, Width = IW, Height = 38, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 0, 3), TabStop = false };
            btnCenter.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btnCenter.Click += (s, e) => RecenterWindow();
            flow.Controls.Add(btnCenter);

            Sep();

            // Reset
            var btnReset = new Button { Text = "Reinitialiser les parametres", ForeColor = Color.FromArgb(220, 80, 80), BackColor = Color.FromArgb(55, 30, 30), FlatStyle = FlatStyle.Flat, Width = IW, Height = 38, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 4, 0, 4), TabStop = false };
            btnReset.FlatAppearance.BorderColor = Color.FromArgb(120, 50, 50);
            btnReset.Click += (s, e) =>
            {
                if (MessageBox.Show("Reinitialiser tous les parametres ?", "Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    numbersOnly = false; alwaysOnTop = true; customFontSize = -1f;
                    colorLeft = Color.FromArgb(100, 200, 255); colorRight = Color.FromArgb(255, 150, 100);
                    titleBarVisible = true; positionLocked = false; interactionLocked = false;
                    this.TopMost = true; this.FormBorderStyle = FormBorderStyle.Sizable;
                    labelLeft.ForeColor = colorLeft; labelRight.ForeColor = colorRight;
                    var wa2 = Screen.PrimaryScreen.WorkingArea;
                    this.Size = new Size(340, 200);
                    this.Location = new Point(wa2.Width / 4, wa2.Height / 8);
                    if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                    RefreshDisplay();
                    settingsForm.Close();
                }
            };
            flow.Controls.Add(btnReset);

            flow.PerformLayout();
            settingsForm.ClientSize = new Size(SW, flow.PreferredSize.Height + 32);
            settingsForm.Show();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CALCUL FONT
        // ═════════════════════════════════════════════════════════════════════

        private float CalcFont()
        {
            if (customFontSize > 0) return customFontSize;
            float h = this.ClientSize.Height;
            float w = this.ClientSize.Width;
            return Math.Max(8f, Math.Min(Math.Min(h / 5f, w / 10f), 28f));
        }

        // ═════════════════════════════════════════════════════════════════════
        //  APPLIQUER OPTIONS
        // ═════════════════════════════════════════════════════════════════════

        private void ApplyTitleBar()
        {
            this.FormBorderStyle = titleBarVisible ? FormBorderStyle.Sizable : FormBorderStyle.None;
        }

        private void ApplyInteractionLock()
        {
            int ex = NativeMethods.GetWindowLong(this.Handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(this.Handle, NativeMethods.GWL_EXSTYLE,
                interactionLocked ? ex | NativeMethods.WS_EX_TRANSPARENT : ex & ~NativeMethods.WS_EX_TRANSPARENT);
            gearLabel.Visible = !interactionLocked;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  HOOK
        // ═════════════════════════════════════════════════════════════════════

        private void SetupHook()
        {
            hookProc = HookCallback;
            hookId = NativeMethods.SetHook(hookProc);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN) leftClicks.Add(DateTime.Now);
                else if (wParam == (IntPtr)NativeMethods.WM_RBUTTONDOWN) rightClicks.Add(DateTime.Now);
            }
            return NativeMethods.CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TIMER & AFFICHAGE
        // ═════════════════════════════════════════════════════════════════════

        private void SetupTimer()
        {
            updateTimer.Interval = 100;
            updateTimer.Tick += (s, e) => { RefreshDisplay(); CheckOpenSettingsFlag(); };
            updateTimer.Start();
        }

        private void CheckOpenSettingsFlag()
        {
            var fp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "open_settings.flag");
            if (!File.Exists(fp)) return;
            try { File.Delete(fp); } catch { return; }
            this.BringToFront();
            OpenSettings();
        }

        private void RefreshDisplay()
        {
            if (positionLocked && this.Location != lockedPosition)
                this.Location = lockedPosition;

            var cutoff = DateTime.Now.AddSeconds(-1);
            leftClicks.RemoveAll(t => t < cutoff);
            rightClicks.RemoveAll(t => t < cutoff);

            int h = this.ClientSize.Height;
            int w = this.ClientSize.Width;
            float fs = CalcFont();
            var f = new Font("Segoe UI", fs, FontStyle.Bold);

            labelLeft.Text = numbersOnly ? $"{leftClicks.Count}" : $"Left\n{leftClicks.Count}";
            labelLeft.Font = f;
            labelLeft.Size = new Size(w / 2, h);
            labelLeft.Location = Point.Empty;

            labelRight.Text = numbersOnly ? $"{rightClicks.Count}" : $"Right\n{rightClicks.Count}";
            labelRight.Font = f;
            labelRight.Size = new Size(w / 2, h);
            labelRight.Location = new Point(w / 2, 0);

            // Gear position
            var scrn = Screen.FromControl(this).WorkingArea;
            var winRC = this.RectangleToScreen(this.ClientRectangle);
            int visLeft = Math.Max(0, scrn.Left - winRC.Left);
            int visTop = Math.Max(0, scrn.Top - winRC.Top);
            int visRight = Math.Min(w, scrn.Right - winRC.Left);
            int visBot = Math.Min(h, scrn.Bottom - winRC.Top);

            if (visRight > visLeft && visBot > visTop)
            {
                int gx = Math.Max(visLeft + 6, visRight - gearLabel.Width - 6);
                int gy = Math.Max(visTop + 6, visTop + 6);
                gx = Math.Max(0, Math.Min(gx, w - gearLabel.Width));
                gy = Math.Max(0, Math.Min(gy, h - gearLabel.Height));
                gearLabel.Location = new Point(gx, gy);
            }
            this.Controls.SetChildIndex(gearLabel, 0);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            NativeMethods.UnhookWindowsHookEx(hookId);
            updateTimer.Stop();
            trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        private void Form1_Load(object sender, EventArgs e) { }
    }

    internal static class NativeMethods
    {
        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        public static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            using var m = p.MainModule!;
            return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(m.ModuleName!), 0);
        }

        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}