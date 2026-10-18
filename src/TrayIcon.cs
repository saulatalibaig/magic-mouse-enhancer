using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MagicMouseEnhancer {
    /// <summary>System-tray presence: status, enable/disable, settings, exit.</summary>
    public class TrayIcon : IDisposable {
        readonly AppController _controller;
        readonly NotifyIcon _icon;
        readonly ContextMenuStrip _menu;
        readonly ToolStripMenuItem _status;
        readonly ToolStripMenuItem _toggle;
        readonly Timer _refreshTimer;
        SettingsForm _form;
        bool _wasConnected;

        public TrayIcon(AppController controller) {
            _controller = controller;

            _menu = new ContextMenuStrip();
            _status = new ToolStripMenuItem("No Magic Mouse detected");
            _status.Enabled = false;
            _toggle = new ToolStripMenuItem("Enabled", null, delegate { ToggleEnabled(); });
            _toggle.Checked = controller.Settings.Enabled;
            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(); });
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit", null, delegate { ExitApp(); });
            _menu.Items.Add(_status);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_toggle);
            _menu.Items.Add(settingsItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _icon = new NotifyIcon();
            _icon.Icon = LoadIcon();
            _icon.Text = "Magic Mouse Enhancer";
            _icon.ContextMenuStrip = _menu;
            _icon.Visible = true;
            _icon.DoubleClick += delegate { ShowSettings(); };

            _refreshTimer = new Timer();
            _refreshTimer.Interval = 3000;
            _refreshTimer.Tick += delegate { UpdateStatus(); };
            _refreshTimer.Start();

            _controller.ConnectionChanged += OnConnectionChanged;
            _controller.BatteryAlert += OnBatteryAlert;
            UpdateStatus();
        }

        void OnConnectionChanged() {
            try {
                if (_menu.IsHandleCreated) {
                    _menu.BeginInvoke((MethodInvoker)UpdateStatus);
                } else {
                    UpdateStatus();
                }
            } catch { }
        }

        void UpdateStatus() {
            try {
                string status = _controller.StatusText;
                _status.Text = status;
                _toggle.Checked = _controller.Settings.Enabled;
                _icon.Text = "Magic Mouse Enhancer — " + status;

                bool connected = status.StartsWith("Connected");
                if (connected && !_wasConnected) {
                    _icon.ShowBalloonTip(3000, "Magic Mouse Enhancer",
                        "Apple Magic Mouse connected — scrolling and gestures are active.",
                        ToolTipIcon.Info);
                }
                _wasConnected = connected;
            } catch { }
        }

        void OnBatteryAlert(int pct) {
            try {
                _icon.ShowBalloonTip(8000, "Magic Mouse Enhancer — low battery",
                    "Apple Magic Mouse battery is at " + pct + "%. Please charge it (connect the USB cable).",
                    ToolTipIcon.Warning);
            } catch { }
        }

        void ToggleEnabled() {
            _controller.Settings.Enabled = !_controller.Settings.Enabled;
            _controller.Settings.Save();
            UpdateStatus();
        }

        public void ShowSettings() {
            try {
                if (_form == null || _form.IsDisposed) {
                    _form = new SettingsForm(_controller);
                    _form.FormClosed += delegate { _form = null; };
                    _form.Show();
                } else {
                    _form.Activate();
                }
            } catch { }
        }

        void ExitApp() {
            try { _icon.Visible = false; } catch { }
            Application.Exit();
        }

        static Icon LoadIcon() {
            try {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(p)) return new Icon(p);
            } catch { }
            return SystemIcons.Application;
        }

        public void Dispose() {
            if (_refreshTimer != null) { try { _refreshTimer.Stop(); _refreshTimer.Dispose(); } catch { } }
            if (_icon != null) { try { _icon.Visible = false; _icon.Dispose(); } catch { } }
            if (_menu != null) { try { _menu.Dispose(); } catch { } }
        }
    }
}
