using System;
using System.Collections.Generic;
using System.Threading;
using MagicMouseEnhancer.Core;
using MagicMouseEnhancer.Hid;

namespace MagicMouseEnhancer {
    /// <summary>
    /// Glue between the HID layer and the gesture engine: scans for a paired Magic
    /// Mouse, opens every matching HID interface, feeds decoded reports into the
    /// engine and forwards engine output to Windows input.
    /// </summary>
    public class AppController : IDisposable {
        readonly object _lock = new object();
        readonly List<DeviceReader> _readers = new List<DeviceReader>();
        readonly GestureEngine _engine;
        readonly EventGestureEngine _eventEngine;
        MouseHook _hook;
        Timer _scanTimer;
        Timer _batteryTimer;
        int _lastBatteryPct = -1;
        bool _lowBatteryNotified;
        volatile bool _disposed;
        bool _loggedInitialScan;
        bool _driverMode;

        public Settings Settings { get; private set; }
        public string StatusText {
            get {
                lock (_lock) {
                    if (_readers.Count > 0) {
                        return "Connected: " + MagicMouse.DeviceName(_readers[0].Pid) + " — gestures active (raw HID)";
                    }
                    if (_driverMode) {
                        return "Connected (Apple driver): " + MagicMouse.DeviceName(MagicMouse.PID_MAGICMOUSE2)
                            + " — swipe gestures active";
                    }
                    return "No Magic Mouse detected — pair it in Bluetooth settings";
                }
            }
        }

        /// <summary>Raised on a worker thread whenever connection state changes.</summary>
        public event Action ConnectionChanged;

        public AppController() {
            Settings = Settings.Load();
            _engine = new GestureEngine(new SendInputSink(), Settings);
            _eventEngine = new EventGestureEngine(Settings);
            _hook = new MouseHook(_eventEngine);
        }

        public void Start() {
            _scanTimer = new Timer(delegate { ScanNow(); }, null, 0, 2000);
            if (Settings.BatteryAlerts) {
                _batteryTimer = new Timer(delegate { CheckBattery(); }, null, 30000, 600000);
            }
        }

        void CheckBattery() {
            try {
                lock (_lock) {
                    ushort vid = 0, pid = 0;
                    if (_readers.Count > 0) { vid = 0x05AC; pid = _readers[0].Pid; }
                    else if (_driverMode) { vid = 0x004C; pid = MagicMouse.PID_MAGICMOUSE2; }
                    else return;
                    int pct = BatteryMonitor.ReadBatteryPct(vid, pid);
                    _lastBatteryPct = pct;
                    if (pct < 0) return;
                    Log.Info("Battery: " + pct + "%");
                    if (pct <= Settings.BatteryAlertLevel && !_lowBatteryNotified) {
                        _lowBatteryNotified = true;
                        FireBatteryAlert(pct);
                    } else if (pct > Settings.BatteryAlertLevel) {
                        _lowBatteryNotified = false;
                    }
                }
            } catch { }
        }

        public int BatteryPct { get { return _lastBatteryPct; } }

        public event Action<int> BatteryAlert;

        void ScanNow() {
            if (_disposed) return;
            bool changed = false;
            int hidCount = -1;
            lock (_lock) {
                List<string> paths = HidApi.EnumerateHidDevicePaths();
                hidCount = paths.Count;
                bool sawMagicMouse = false;
                if (_readers.Count == 0) {
                    foreach (string path in paths) {
                        try {
                            ushort vid, pid;
                            if (!TryReadAttributes(path, out vid, out pid)) continue;
                            if (!MagicMouse.IsMagicMouse(vid, pid)) continue;
                            sawMagicMouse = true;
                            DeviceReader reader = new DeviceReader();
                            if (reader.Open(path, vid, pid)) {
                                reader.ReportReceived += OnReport;
                                reader.Faulted += OnFaulted;
                                _readers.Add(reader);
                                Log.Info("Connected " + MagicMouse.DeviceName(pid) + " [" + path + "]");
                                changed = true;
                            }
                        } catch (Exception ex) {
                            Log.Error("scan: " + ex.Message);
                        }
                    }
                } else {
                    // already have readers; just note whether the device is still around
                    foreach (string path in paths) {
                        if (path.IndexOf("004c", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            path.IndexOf("05ac", StringComparison.OrdinalIgnoreCase) >= 0) {
                            sawMagicMouse = true;
                            break;
                        }
                    }
                }

                // Driver mode: the Apple kernel driver owns the device, so raw
                // HID opens fail (win32 5) but the device is present. Fall back
                // to the event-hook layer for swipe gestures.
                bool driverMode = false;
                if (_readers.Count == 0) {
                    foreach (string path in paths) {
                        if (IsMagicMousePath(path)) { sawMagicMouse = true; break; }
                    }
                    if (sawMagicMouse) driverMode = true;
                }
                if (driverMode != _driverMode) {
                    _driverMode = driverMode;
                    if (driverMode) {
                        Log.Info("Apple kernel driver owns the device — switching to event-hook gesture mode");
                        _hook.SetEnabled(Settings.Enabled);
                        _hook.Start();
                        changed = true;
                    } else {
                        _hook.Stop();
                        changed = true;
                    }
                }
            }
            if (!_loggedInitialScan) {
                _loggedInitialScan = true;
                Log.Info(hidCount >= 0
                    ? "Initial scan: " + hidCount + " HID interface(s) visible, " + _readers.Count + " Magic Mouse interface(s) opened, driverMode=" + _driverMode
                    : "Initial scan: already connected — " + _readers.Count + " Magic Mouse interface(s) active");
            }
            if (changed) FireConnectionChanged();
        }

        static bool IsMagicMousePath(string path) {
            // Paths look like ..._vid&0001004c_pid&0269#... or ...vid_05ac&pid_030d...
            string p = path.ToLowerInvariant();
            bool apple = p.IndexOf("004c", StringComparison.Ordinal) >= 0 || p.IndexOf("05ac", StringComparison.Ordinal) >= 0;
            bool mm = p.IndexOf("0269", StringComparison.Ordinal) >= 0
                   || p.IndexOf("030d", StringComparison.Ordinal) >= 0
                   || p.IndexOf("029f", StringComparison.Ordinal) >= 0;
            return apple && mm;
        }

        static bool TryReadAttributes(string path, out ushort vid, out ushort pid) {
            vid = 0; pid = 0;
            IntPtr h = HidApi.CreateFile(path, HidApi.GENERIC_READ, HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE,
                IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            if (HidApi.IsInvalidHandle(h)) return false;
            try {
                HidApi.HIDD_ATTRIBUTES attrs = new HidApi.HIDD_ATTRIBUTES();
                attrs.Size = Marshal_SizeOfAttributes();
                if (!HidApi.HidD_GetAttributes(h, ref attrs)) return false;
                vid = attrs.VendorID;
                pid = attrs.ProductID;
                return true;
            } finally {
                HidApi.CloseHandle(h);
            }
        }

        static int Marshal_SizeOfAttributes() {
            // HIDD_ATTRIBUTES is {int Size; ushort Vid; ushort Pid; ushort Version}
            return 4 + 2 + 2 + 2;
        }

        void OnReport(byte[] data, int size) {
            if (!Settings.Enabled) return;
            TouchReport report = TouchDecoder.Decode(data, size);
            if (report == null) return;
            _engine.Process(report);
        }

        void OnFaulted(DeviceReader reader) {
            bool changed = false;
            lock (_lock) {
                if (_readers.Remove(reader)) {
                    Log.Info("Disconnected " + MagicMouse.DeviceName(reader.Pid));
                    changed = true;
                }
                reader.Dispose();
            }
            if (changed) FireConnectionChanged();
        }

        void FireConnectionChanged() {
            Action h = ConnectionChanged;
            if (h != null) {
                try { h(); } catch { }
            }
        }

        void FireBatteryAlert(int pct) {
            Action<int> h = BatteryAlert;
            if (h != null) {
                try { h(pct); } catch { }
            }
        }

        public void Dispose() {
            _disposed = true;
            if (_scanTimer != null) { try { _scanTimer.Dispose(); } catch { } }
            if (_batteryTimer != null) { try { _batteryTimer.Dispose(); } catch { } }
            lock (_lock) {
                foreach (DeviceReader r in _readers) r.Dispose();
                _readers.Clear();
            }
            if (_hook != null) { try { _hook.Dispose(); } catch { } }
        }
    }
}
