using System;
using System.Runtime.InteropServices;
using System.Threading;
using MagicMouseEnhancer.Hid;

namespace MagicMouseEnhancer {
    /// <summary>
    /// Opens one HID interface of a Magic Mouse, switches it into multitouch mode
    /// (feature report — this is what makes the touch surface data appear) and
    /// streams raw input reports on a background thread.
    /// </summary>
    public class DeviceReader : IDisposable {
        const uint ACCESS_RW = HidApi.GENERIC_READ | HidApi.GENERIC_WRITE;
        const uint ACCESS_R = HidApi.GENERIC_READ;
        const uint SHARE = HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE;

        IntPtr _handle = IntPtr.Zero;
        Thread _thread;
        volatile bool _stop;
        readonly byte[] _buffer = new byte[512];

        public ushort Vid { get; private set; }
        public ushort Pid { get; private set; }
        public string DevicePath { get; private set; }
        public bool Opened { get { return !HidApi.IsInvalidHandle(_handle); } }

        /// <summary>Raw input reports (first byte is the HID report id).</summary>
        public event Action<byte[], int> ReportReceived;
        /// <summary>Raised on the reader thread when the device disappears or read fails.</summary>
        public event Action<DeviceReader> Faulted;

        public bool Open(string path, ushort vid, ushort pid) {
            _handle = HidApi.CreateFile(path, ACCESS_RW, SHARE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            if (HidApi.IsInvalidHandle(_handle)) {
                _handle = HidApi.CreateFile(path, ACCESS_R, SHARE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
                if (HidApi.IsInvalidHandle(_handle)) return false;
            }
            Vid = vid;
            Pid = pid;
            DevicePath = path;
            // The touch data lives on the vendor-defined collection (col02+). The
            // col01 mouse collection would reject the multitouch feature report.
            if (path.IndexOf("&col01", StringComparison.OrdinalIgnoreCase) < 0) {
                SendFeatureEnable();
                StartMtRetry();
            }
            Log.Info("Opened " + path);
            _thread = new Thread(ReadLoop);
            _thread.IsBackground = true;
            _thread.Name = "MM-HID-Reader";
            _thread.Start();
            return true;
        }

        /// <summary>
        /// The Magic Mouse only reports touch data after being switched into
        /// multitouch mode via a HID feature report:
        ///   - Magic Mouse 2 / 2 USB-C:  F1 02 01
        ///   - Magic Mouse 1:            D7 01
        /// (Same reports the Linux kernel driver sends; retried like the kernel does.)
        /// NOTE: Windows' Bluetooth HID stack (hidbth) rejects feature reports from
        /// user mode with ERROR_INVALID_FUNCTION (win32 1) — a known platform
        /// limitation. Over USB the same call succeeds. We retry periodically so a
        /// USB reconnect or a freshly woken device gets picked up.
        /// </summary>
        void SendFeatureEnable() {
            byte[] f;
            string label;
            if (Pid == MagicMouse.PID_MAGICMOUSE2 || Pid == MagicMouse.PID_MAGICMOUSE2_USBC) {
                f = new byte[] { 0xF1, 0x02, 0x01 };
                label = "F1 02 01";
            } else if (Pid == MagicMouse.PID_MAGICMOUSE1) {
                f = new byte[] { 0xD7, 0x01 };
                label = "D7 01";
            } else {
                return;
            }

            bool sent = false;
            for (int attempt = 0; attempt < 3; attempt++) {
                if (HidApi.HidD_SetFeature(_handle, f, f.Length)) { sent = true; break; }
                int err = Marshal.GetLastWin32Error();
                if (attempt == 0) Log.Warn("Feature report " + label + " attempt 1 failed (win32 " + err + "), retrying…");
                Thread.Sleep(500);
            }
            if (sent) {
                Log.Info("Multitouch enabled — feature report " + label + " accepted (PID " + Pid.ToString("X4") + ")");
                _mtEnabled = true;
            } else {
                bool overBt = DevicePath.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0
                           || DevicePath.IndexOf("00001124", StringComparison.OrdinalIgnoreCase) >= 0;
                Log.Error(overBt
                    ? "Cannot enable multitouch over Bluetooth (win32 1 = Windows blocks feature reports on BT HID). "
                      + "Connect the mouse with its USB cable for full touch support, or install the Apple Bluetooth driver (see README)."
                    : "Could not switch PID " + Pid.ToString("X4") + " into multitouch mode (" + label + ").");
            }
        }

        // Retry enabling multitouch every 10 s in case the device was asleep or
        // was just reconnected (USB). Cheap and idempotent.
        Timer _mtRetryTimer;
        volatile bool _mtEnabled;

        void StartMtRetry() {
            if (_mtRetryTimer != null) return;
            _mtRetryTimer = new Timer(delegate {
                if (_mtEnabled || _stop) return;
                if (!HidApi.IsInvalidHandle(_handle)) SendFeatureEnable();
            }, null, 10000, 10000);
        }

        void ReadLoop() {
            while (!_stop) {
                uint read = 0;
                if (HidApi.ReadFile(_handle, _buffer, (uint)_buffer.Length, out read, IntPtr.Zero)) {
                    if (read > 0) {
                        Action<byte[], int> h = ReportReceived;
                        if (h != null) h(_buffer, (int)read);
                    }
                } else {
                    Log.Info("HID read ended (win32 error " + Marshal.GetLastWin32Error() + ")");
                    break;
                }
            }
            Action<DeviceReader> f = Faulted;
            if (f != null) f(this);
        }

        public void Dispose() {
            _stop = true;
            if (_mtRetryTimer != null) { try { _mtRetryTimer.Dispose(); } catch { } _mtRetryTimer = null; }
            if (!HidApi.IsInvalidHandle(_handle)) {
                HidApi.CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
            if (_thread != null && _thread.IsAlive) {
                try { _thread.Join(500); } catch { }
            }
        }
    }
}
