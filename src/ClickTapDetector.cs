using System;
using MagicMouseEnhancer.Core;

namespace MagicMouseEnhancer {
    /// <summary>
    /// Smart Zoom over Bluetooth (driver mode). The Apple kernel driver maps a
    /// two-finger tap to a LEFT CLICK (L_DOWN/L_UP), so a two-finger double-tap
    /// arrives as a plain double-click. This detector recognizes the second
    /// click of a double-click (within SmartZoomClickWindowMs of the first
    /// click's release) and reports it as a Smart Zoom.
    ///
    /// TRADE-OFF (documented): the driver erases touch count, so with Smart
    /// Zoom enabled over Bluetooth, ANY double-click zooms instead of acting as
    /// a normal double-click. This matches the macOS "smart zoom" mental model.
    /// USB / raw-HID mode keeps the true two-finger double-tap detection
    /// (GestureEngine) where the touch count is available.
    ///
    /// Pure logic — the self-test drives it with synthetic click pairs.
    /// </summary>
    public class ClickTapDetector {
        public const int SmartZoomClickWindowMs = 400; // max gap click-release -> next press

        readonly Settings _settings;
        readonly object _sync = new object();
        long _lastClickUpMs;
        bool _suppressNextUp;
        long _fakeClock; // 0 = real clock (self-test sets this)

        public ClickTapDetector(Settings settings) {
            _settings = settings;
        }

        public void SetClock(long ms) { _fakeClock = ms; }

        long NowMs() {
            if (_fakeClock != 0) return _fakeClock;
            return Environment.TickCount & 0x7FFFFFFF;
        }

        /// <summary>Feed a button event. Returns true if the event must be
        /// swallowed; sets zoom=true when this event completes a Smart Zoom.</summary>
        public bool OnButton(bool isDown, out bool zoom) {
            zoom = false;
            lock (_sync) {
                long now = NowMs();
                if (isDown) {
                    if (_settings.SmartZoom && _lastClickUpMs != 0
                        && (now - _lastClickUpMs) <= SmartZoomClickWindowMs) {
                        // second press within the window: double-tap -> Smart Zoom
                        _lastClickUpMs = 0;
                        _suppressNextUp = true;
                        zoom = true;
                        return true;   // swallow this press
                    }
                    return false;      // normal press, pass through
                }
                if (_suppressNextUp) {
                    _suppressNextUp = false;
                    return true;       // swallow the release paired with the zoomed press
                }
                _lastClickUpMs = now;
                return false;
            }
        }

        public void Reset() {
            lock (_sync) {
                _lastClickUpMs = 0;
                _suppressNextUp = false;
            }
        }
    }
}
