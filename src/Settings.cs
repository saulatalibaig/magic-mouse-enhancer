using System;
using System.Reflection;
using Microsoft.Win32;

namespace MagicMouseEnhancer.Core {
    /// <summary>
    /// User settings persisted in HKCU\Software\MagicMouseEnhancer.
    /// Stored in the registry so there is no file-lock or permission issue and
    /// settings survive uninstall/reinstall of the app itself.
    /// </summary>
    public class Settings {
        const string RootKeyName = @"Software\MagicMouseEnhancer";

        readonly string _keyName;

        public bool Enabled = true;                  // master switch
        public bool NaturalScroll = false;           // macOS-style inverted scrolling
        public int Sensitivity = 5;                  // 1 (slow) .. 10 (fast)
        public bool HorizontalScroll = true;         // emit horizontal wheel events
        public bool SwipeFromHorizontalWheel = true; // driver mode: short HWHEEL bursts = two-finger swipes
        public bool PinchZoom = true;                // two-finger spread/close -> Ctrl+wheel zoom (raw HID mode)
        public int PinchSensitivity = 5;             // 1-10: touch-units per zoom notch (10 = most sensitive)
        public bool SmartZoom = true;                // two-finger double-tap -> Ctrl+wheel zoom (raw HID mode)
        public bool TapToClick = false;              // one-finger tap = click (USB/raw HID mode)
        public bool TwoFingerTapMiddle = true;       // two-finger tap = middle click (USB/raw HID mode)
        public bool RightClickZone = false;          // click on right third of surface = right click (USB mode)
        public bool BatteryAlerts = true;            // low-battery tray alerts
        public int BatteryAlertLevel = 15;           // % threshold for the low-battery alert
        public bool SmoothScroll = false;            // 60-unit wheel granularity
        public bool WindowsScrollPassthrough = false;// let Windows' own driver scroll
        public GestureAction SwipeLeft = GestureAction.Back;
        public GestureAction SwipeRight = GestureAction.Forward;
        public GestureAction SwipeUp = GestureAction.TaskView;
        public GestureAction SwipeDown = GestureAction.ShowDesktop;
        public bool AutoStart = false;               // HKCU Run key

        public Settings() : this(RootKeyName) { }

        public Settings(string keyName) { _keyName = keyName; }

        public static Settings Load() {
            Settings s = new Settings();
            s.Read();
            return s;
        }

        /// <summary>Load from an explicit registry key (used by the self-test).</summary>
        public static Settings LoadFrom(string keyName) {
            Settings s = new Settings(keyName);
            s.Read();
            return s;
        }

        /// <summary>Re-read values from the registry into this instance.</summary>
        public void Reload() { Read(); }

        void Read() {
            try {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(_keyName)) {
                    if (k == null) return;
                    Enabled = ReadBool(k, "Enabled", true);
                    NaturalScroll = ReadBool(k, "NaturalScroll", false);
                    Sensitivity = Clamp(ReadInt(k, "Sensitivity", 5), 1, 10);
                    HorizontalScroll = ReadBool(k, "HorizontalScroll", true);
                    SwipeFromHorizontalWheel = ReadBool(k, "SwipeFromHorizontalWheel", true);
                    PinchZoom = ReadBool(k, "PinchZoom", true);
                    PinchSensitivity = Clamp(ReadInt(k, "PinchSensitivity", 5), 1, 10);
                    SmartZoom = ReadBool(k, "SmartZoom", true);
                    TapToClick = ReadBool(k, "TapToClick", false);
                    TwoFingerTapMiddle = ReadBool(k, "TwoFingerTapMiddle", true);
                    RightClickZone = ReadBool(k, "RightClickZone", false);
                    BatteryAlerts = ReadBool(k, "BatteryAlerts", true);
                    BatteryAlertLevel = Clamp(ReadInt(k, "BatteryAlertLevel", 15), 5, 50);
                    SmoothScroll = ReadBool(k, "SmoothScroll", false);
                    WindowsScrollPassthrough = ReadBool(k, "WindowsScrollPassthrough", false);
                    SwipeLeft = ReadAction(k, "SwipeLeft", GestureAction.Back);
                    SwipeRight = ReadAction(k, "SwipeRight", GestureAction.Forward);
                    SwipeUp = ReadAction(k, "SwipeUp", GestureAction.TaskView);
                    SwipeDown = ReadAction(k, "SwipeDown", GestureAction.ShowDesktop);
                    AutoStart = ReadBool(k, "AutoStart", false);
                }
            } catch { }
        }

        public void Save() {
            try {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(_keyName)) {
                    k.SetValue("Enabled", Enabled ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("NaturalScroll", NaturalScroll ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("Sensitivity", Sensitivity, RegistryValueKind.DWord);
                    k.SetValue("HorizontalScroll", HorizontalScroll ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("SwipeFromHorizontalWheel", SwipeFromHorizontalWheel ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("PinchZoom", PinchZoom ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("PinchSensitivity", PinchSensitivity, RegistryValueKind.DWord);
                    k.SetValue("SmartZoom", SmartZoom ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("TapToClick", TapToClick ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("TwoFingerTapMiddle", TwoFingerTapMiddle ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("RightClickZone", RightClickZone ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("BatteryAlerts", BatteryAlerts ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("BatteryAlertLevel", BatteryAlertLevel, RegistryValueKind.DWord);
                    k.SetValue("SmoothScroll", SmoothScroll ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("WindowsScrollPassthrough", WindowsScrollPassthrough ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("SwipeLeft", (int)SwipeLeft, RegistryValueKind.DWord);
                    k.SetValue("SwipeRight", (int)SwipeRight, RegistryValueKind.DWord);
                    k.SetValue("SwipeUp", (int)SwipeUp, RegistryValueKind.DWord);
                    k.SetValue("SwipeDown", (int)SwipeDown, RegistryValueKind.DWord);
                    k.SetValue("AutoStart", AutoStart ? 1 : 0, RegistryValueKind.DWord);
                }
            } catch { }
        }

        /// <summary>Add/remove the "run at login" entry for the current executable.</summary>
        public void ApplyAutoStart() {
            try {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (run == null) return;
                    if (AutoStart) {
                        string exe = "\"" + Assembly.GetEntryAssembly().Location + "\"";
                        run.SetValue("MagicMouseEnhancer", exe, RegistryValueKind.String);
                    } else {
                        run.DeleteValue("MagicMouseEnhancer", false);
                    }
                }
            } catch { }
        }

        static int ReadInt(RegistryKey k, string name, int def) {
            object o = k.GetValue(name);
            if (o == null) return def;
            try { return Convert.ToInt32(o); } catch { return def; }
        }

        static bool ReadBool(RegistryKey k, string name, bool def) {
            return ReadInt(k, name, def ? 1 : 0) != 0;
        }

        static GestureAction ReadAction(RegistryKey k, string name, GestureAction def) {
            int v = ReadInt(k, name, (int)def);
            if (v < 0 || v > (int)GestureAction.End) return def;
            return (GestureAction)v;
        }

        static int Clamp(int v, int lo, int hi) {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
