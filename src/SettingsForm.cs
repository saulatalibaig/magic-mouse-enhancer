using System;
using System.Drawing;
using System.Windows.Forms;
using MagicMouseEnhancer.Core;

namespace MagicMouseEnhancer {
    /// <summary>Settings window (built in code — no designer dependency).</summary>
    public class SettingsForm : Form {
        readonly AppController _controller;
        readonly Timer _statusTimer;

        CheckBox _enabled;
        CheckBox _natural;
        CheckBox _passthrough;
        CheckBox _hScroll;
        CheckBox _pinch;
        CheckBox _smartZoom;
        CheckBox _tapToClick;
        CheckBox _twoFingerMiddle;
        CheckBox _rightZone;
        CheckBox _batteryAlerts;
        CheckBox _smooth;
        CheckBox _autoStart;
        TrackBar _sensitivity;
        Label _sensValue;
        TrackBar _pinchSensitivity;
        Label _pinchSensValue;
        Label _statusLabel;
        ComboBox _swipeLeft;
        ComboBox _swipeRight;
        ComboBox _swipeUp;
        ComboBox _swipeDown;

        public SettingsForm(AppController controller) {
            _controller = controller;

            Text = "Magic Mouse Enhancer — Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 800);
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            BuildUi();
            LoadValues();

            _statusTimer = new Timer();
            _statusTimer.Interval = 2000;
            _statusTimer.Tick += delegate { UpdateStatus(); };
            _statusTimer.Start();
            UpdateStatus();
        }

        void BuildUi() {
            int y = 16;

            Label title = new Label();
            title.Text = "Magic Mouse Enhancer";
            title.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            title.Location = new Point(16, y);
            title.AutoSize = true;
            Controls.Add(title);
            y += 26;

            Label subtitle = new Label();
            subtitle.Text = "Full Apple Magic Mouse functionality on Windows 11 — scroll, horizontal\nscroll and two-finger swipe gestures. Runs quietly in the system tray.";
            subtitle.ForeColor = Color.DimGray;
            subtitle.Location = new Point(16, y);
            subtitle.AutoSize = true;
            Controls.Add(subtitle);
            y += 52;

            _statusLabel = new Label();
            _statusLabel.Location = new Point(16, y);
            _statusLabel.AutoSize = true;
            _statusLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Controls.Add(_statusLabel);
            y += 30;

            _enabled = AddCheck("Enabled (start / stop gesture processing)", y); y += 26;
            _natural = AddCheck("Natural scrolling (macOS style — content follows your finger)", y); y += 26;
            _passthrough = AddCheck("Let Windows handle scrolling (app only adds gestures)", y); y += 26;
            _hScroll = AddCheck("Two-finger horizontal swipe → Back/Forward (driver mode)", y); y += 26;
            _pinch = AddCheck("Pinch to zoom (two-finger spread/close → Ctrl+wheel, USB mode)", y); y += 26;
            _smartZoom = AddCheck("Smart zoom (double-tap/double-click → zoom)", y); y += 26;
            _tapToClick = AddCheck("Tap to click (one-finger tap = click, USB mode)", y); y += 26;
            _twoFingerMiddle = AddCheck("Two-finger tap = middle click (USB mode)", y); y += 26;
            _rightZone = AddCheck("Right third of surface = right click (USB mode)", y); y += 26;
            _batteryAlerts = AddCheck("Low-battery alerts (tray notification)", y); y += 34;
            _smooth = AddCheck("Smooth scrolling (finer wheel steps for modern apps)", y); y += 34;

            Label sensLabel = new Label();
            sensLabel.Text = "Scroll sensitivity:";
            sensLabel.Location = new Point(16, y + 8);
            sensLabel.AutoSize = true;
            Controls.Add(sensLabel);

            _sensitivity = new TrackBar();
            _sensitivity.Minimum = 1;
            _sensitivity.Maximum = 10;
            _sensitivity.TickFrequency = 1;
            _sensitivity.Location = new Point(150, y);
            _sensitivity.Width = 200;
            _sensitivity.TickStyle = TickStyle.BottomRight;
            _sensitivity.ValueChanged += delegate { _sensValue.Text = _sensitivity.Value.ToString(); };
            Controls.Add(_sensitivity);

            _sensValue = new Label();
            _sensValue.Location = new Point(356, y + 8);
            _sensValue.AutoSize = true;
            Controls.Add(_sensValue);
            y += 34;

            Label pinchSensLabel = new Label();
            pinchSensLabel.Text = "Pinch sensitivity:";
            pinchSensLabel.Location = new Point(16, y + 8);
            pinchSensLabel.AutoSize = true;
            Controls.Add(pinchSensLabel);

            _pinchSensitivity = new TrackBar();
            _pinchSensitivity.Minimum = 1;
            _pinchSensitivity.Maximum = 10;
            _pinchSensitivity.TickFrequency = 1;
            _pinchSensitivity.Location = new Point(150, y);
            _pinchSensitivity.Width = 200;
            _pinchSensitivity.TickStyle = TickStyle.BottomRight;
            _pinchSensitivity.ValueChanged += delegate { _pinchSensValue.Text = _pinchSensitivity.Value.ToString(); };
            Controls.Add(_pinchSensitivity);

            _pinchSensValue = new Label();
            _pinchSensValue.Location = new Point(356, y + 8);
            _pinchSensValue.AutoSize = true;
            Controls.Add(_pinchSensValue);
            y += 48;

            Label gestures = new Label();
            gestures.Text = "Swipe actions (driver mode: two-finger left/right)";
            gestures.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            gestures.Location = new Point(16, y);
            gestures.AutoSize = true;
            Controls.Add(gestures);
            y += 26;

            Label gestureNote = new Label();
            gestureNote.Text = "In driver mode, vertical swipe gestures are handled by the Apple\n"
                + "driver itself (Task view / Show desktop). The app maps the\n"
                + "two-finger horizontal swipe to the actions below.";
            gestureNote.ForeColor = Color.DimGray;
            gestureNote.Location = new Point(16, y);
            gestureNote.AutoSize = true;
            Controls.Add(gestureNote);
            y += 58;

            _swipeLeft = AddComboRow("Swipe left:", ref y);
            _swipeRight = AddComboRow("Swipe right:", ref y);
            _swipeUp = AddComboRow("Swipe up:", ref y);
            _swipeDown = AddComboRow("Swipe down:", ref y);
            y += 8;

            _autoStart = AddCheck("Start with Windows", y); y += 40;

            Button saveBtn = new Button();
            saveBtn.Text = "Save & Close";
            saveBtn.Size = new Size(110, 30);
            saveBtn.Location = new Point(ClientSize.Width - 240, ClientSize.Height - 48);
            saveBtn.Click += delegate { Save(); };
            Controls.Add(saveBtn);

            Button cancelBtn = new Button();
            cancelBtn.Text = "Cancel";
            cancelBtn.Size = new Size(90, 30);
            cancelBtn.Location = new Point(ClientSize.Width - 120, ClientSize.Height - 48);
            cancelBtn.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelBtn);
        }

        CheckBox AddCheck(string text, int y) {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.Location = new Point(16, y);
            cb.AutoSize = true;
            Controls.Add(cb);
            return cb;
        }

        ComboBox AddComboRow(string labelText, ref int y) {
            Label l = new Label();
            l.Text = labelText;
            l.Location = new Point(16, y + 4);
            l.AutoSize = true;
            Controls.Add(l);

            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.Width = 240;
            cb.Location = new Point(150, y);
            GestureAction[] all = GestureActionNames.All;
            foreach (GestureAction a in all) cb.Items.Add(GestureActionNames.Name(a));
            cb.Tag = all;
            Controls.Add(cb);
            y += 30;
            return cb;
        }

        void LoadValues() {
            Settings s = _controller.Settings;
            _enabled.Checked = s.Enabled;
            _natural.Checked = s.NaturalScroll;
            _passthrough.Checked = s.WindowsScrollPassthrough;
            _hScroll.Checked = s.SwipeFromHorizontalWheel;
            _smooth.Checked = s.SmoothScroll;
            _pinch.Checked = s.PinchZoom;
            _smartZoom.Checked = s.SmartZoom;
            _tapToClick.Checked = s.TapToClick;
            _twoFingerMiddle.Checked = s.TwoFingerTapMiddle;
            _rightZone.Checked = s.RightClickZone;
            _batteryAlerts.Checked = s.BatteryAlerts;
            _sensitivity.Value = s.Sensitivity;
            _sensValue.Text = s.Sensitivity.ToString();
            _pinchSensitivity.Value = s.PinchSensitivity;
            _pinchSensValue.Text = s.PinchSensitivity.ToString();
            _autoStart.Checked = s.AutoStart;
            SelectAction(_swipeLeft, s.SwipeLeft);
            SelectAction(_swipeRight, s.SwipeRight);
            SelectAction(_swipeUp, s.SwipeUp);
            SelectAction(_swipeDown, s.SwipeDown);
        }

        static void SelectAction(ComboBox cb, GestureAction action) {
            GestureAction[] all = (GestureAction[])cb.Tag;
            for (int i = 0; i < all.Length; i++) {
                if (all[i] == action) { cb.SelectedIndex = i; return; }
            }
            cb.SelectedIndex = 0;
        }

        static GestureAction SelectedAction(ComboBox cb) {
            GestureAction[] all = (GestureAction[])cb.Tag;
            int i = cb.SelectedIndex;
            if (i < 0 || i >= all.Length) return GestureAction.None;
            return all[i];
        }

        void UpdateStatus() {
            string status = _controller.StatusText;
            bool connected = status.StartsWith("Connected");
            _statusLabel.Text = status;
            _statusLabel.ForeColor = connected ? Color.ForestGreen : Color.Firebrick;
        }

        void Save() {
            Settings s = _controller.Settings;
            s.Enabled = _enabled.Checked;
            s.NaturalScroll = _natural.Checked;
            s.WindowsScrollPassthrough = _passthrough.Checked;
            s.SwipeFromHorizontalWheel = _hScroll.Checked;
            s.SmoothScroll = _smooth.Checked;
            s.PinchZoom = _pinch.Checked;
            s.SmartZoom = _smartZoom.Checked;
            s.TapToClick = _tapToClick.Checked;
            s.TwoFingerTapMiddle = _twoFingerMiddle.Checked;
            s.RightClickZone = _rightZone.Checked;
            s.BatteryAlerts = _batteryAlerts.Checked;
            s.Sensitivity = _sensitivity.Value;
            s.PinchSensitivity = _pinchSensitivity.Value;
            s.SwipeLeft = SelectedAction(_swipeLeft);
            s.SwipeRight = SelectedAction(_swipeRight);
            s.SwipeUp = SelectedAction(_swipeUp);
            s.SwipeDown = SelectedAction(_swipeDown);
            s.AutoStart = _autoStart.Checked;
            s.Save();
            s.ApplyAutoStart();
            s.Reload();
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            if (_statusTimer != null) { try { _statusTimer.Stop(); _statusTimer.Dispose(); } catch { } }
            base.OnFormClosed(e);
        }
    }
}
