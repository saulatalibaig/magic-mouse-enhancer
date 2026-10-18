using System;
using System.Collections.Generic;

namespace MagicMouseEnhancer.Core {
    /// <summary>
    /// Turns decoded touch reports into Windows input:
    ///   - one finger moving  -> mouse wheel / horizontal wheel (like macOS)
    ///   - two fingers swiping -> configurable gesture actions
    /// Scroll math follows the Linux kernel driver (hid-magicmouse.c): a finger-travel
    /// step is divided by a sensitivity denominator and emitted as whole wheel notches.
    /// </summary>
    public class GestureEngine {
        public interface ISink {
            void Scroll(int deltaY, int deltaX);
            void GestureTriggered(GestureAction action);
            void Zoom(int wheelDelta);   // Ctrl+wheel — pinch to zoom
            void MouseButton(int button); // 1=left 2=right 4=middle — tap-to-click / two-finger tap
        }

        class Tracked {
            public int X, Y;          // latest position
            public int AnchorX, AnchorY; // position of last emitted notch
            public double AccX, AccY; // fractional notch carry
            public bool WasDown;
        }

        public const int GestureThreshold = 500; // touch units (~5 mm) before a swipe fires
        public const int SmartZoomTapWindowMs = 320; // max gap between the two taps
        public const int SmartZoomDelta = 240;       // zoom in two notches per double-tap

        readonly ISink _sink;
        readonly Settings _settings;
        readonly object _sync = new object();
        readonly Dictionary<int, Tracked> _touches = new Dictionary<int, Tracked>();
        bool _gestureMode;
        int _gStartX, _gStartY;
        bool _swipeFired;
        long _fakeClock; // 0 = real clock (self-test sets this)

        // pinch state
        bool _pinchBaseSet;
        bool _pinchActive;      // true once a zoom notch actually fired -> this is a pinch, not a swipe
        double _pinchBaseDist;
        double _pinchAcc;

        // smart zoom (two-finger double-tap) state
        int _tapCount;
        long _lastTapEndMs;
        bool _tapCandidate;     // gesture was a clean tap (no swipe, no pinch)

        // one-finger tap state (tap-to-click)
        bool _oneFingerTracked;
        int _oneStartX, _oneStartY;
        long _oneStartMs;
        bool _oneFingerWasTap;

        // touch surface half-width for tap zones (signed 12-bit coords, ~±2048)
        const int SurfaceHalf = 1200;
        const int TapMaxMove = 120;     // touch units a tap may travel
        const int TapMaxMs = 350;       // max tap duration

        public GestureEngine(ISink sink, Settings settings) {
            _sink = sink;
            _settings = settings;
        }

        // Test hook: a non-zero value overrides Environment.TickCount.
        public void SetClock(long ms) { _fakeClock = ms; }

        long NowMs() {
            if (_fakeClock != 0) return _fakeClock;
            return Environment.TickCount & 0x7FFFFFFF;
        }

        public void Reset() {
            lock (_sync) {
                _touches.Clear();
                _gestureMode = false;
                _swipeFired = false;
                _pinchBaseSet = false;
                _pinchActive = false;
                _pinchBaseDist = 0;
                _pinchAcc = 0;
                _tapCount = 0;
                _lastTapEndMs = 0;
                _tapCandidate = false;
                _oneFingerTracked = false;
                _oneFingerWasTap = false;
            }
        }

        public void Process(TouchReport report) {
            lock (_sync) {
                HashSet<int> present = new HashSet<int>();
                foreach (TouchPoint p in report.Touches) {
                    if (!p.Down) continue;
                    present.Add(p.Id);
                    Tracked tr;
                    if (!_touches.TryGetValue(p.Id, out tr)) {
                        tr = new Tracked();
                        tr.AnchorX = tr.X = p.X;
                        tr.AnchorY = tr.Y = p.Y;
                        _touches[p.Id] = tr;
                    }
                    if (p.RawState == TouchDecoder.STATE_START || !tr.WasDown) {
                        tr.AnchorX = p.X;
                        tr.AnchorY = p.Y;
                        tr.AccX = 0;
                        tr.AccY = 0;
                    }
                    tr.WasDown = true;
                    tr.X = p.X;
                    tr.Y = p.Y;
                }

                List<int> dead = new List<int>();
                foreach (int id in _touches.Keys) if (!present.Contains(id)) dead.Add(id);
                foreach (int id in dead) _touches.Remove(id);

                int down = _touches.Count;
                bool wantGesture = down >= 2;

                // ---- one-finger tap detection (tap-to-click) ----
                if (!wantGesture) {
                    if (down == 1) {
                        Tracked single = null;
                        foreach (Tracked t in _touches.Values) { single = t; break; }
                        if (single != null && !_oneFingerTracked) {
                            _oneFingerTracked = true;
                            _oneStartX = single.X;
                            _oneStartY = single.Y;
                            _oneStartMs = NowMs();
                            _oneFingerWasTap = true;
                        } else if (single != null && _oneFingerTracked) {
                            // still down: a moving finger is not a tap
                            if (Math.Abs(single.X - _oneStartX) > TapMaxMove || Math.Abs(single.Y - _oneStartY) > TapMaxMove)
                                _oneFingerWasTap = false;
                        }
                    } else if (down == 0 && _oneFingerTracked) {
                        // finger lifted: was it a tap?
                        bool wasTap = _oneFingerWasTap && (NowMs() - _oneStartMs) <= TapMaxMs;
                        _oneFingerTracked = false;
                        _oneFingerWasTap = false;
                        if (wasTap && _settings.TapToClick) {
                            int button = TapZoneButton(_oneStartX);
                            Log.Info("Tap-to-click: zone " + button + " (x=" + _oneStartX + ")");
                            _sink.MouseButton(button);
                        }
                    }
                } else {
                    _oneFingerTracked = false;   // multi-touch: not a one-finger tap
                    _oneFingerWasTap = false;
                }

                if (wantGesture != _gestureMode) {
                    bool wasGesture = _gestureMode;
                    _gestureMode = wantGesture;
                    _swipeFired = false;
                    _pinchBaseSet = false;
                    _pinchActive = false;
                    _pinchBaseDist = 0;
                    _pinchAcc = 0;
                    if (_gestureMode) {
                        _tapCandidate = true;   // might become a tap if nothing fires
                        _gStartX = CentroidX();
                        _gStartY = CentroidY();
                    } else if (wasGesture) {
                        // two-finger gesture just ended: was it a clean tap?
                        if (_tapCandidate) {
                            long now = NowMs();
                            if (_settings.SmartZoom && now - _lastTapEndMs <= SmartZoomTapWindowMs) {
                                // second tap within the window -> Smart Zoom
                                _tapCount = 0;
                                _lastTapEndMs = 0;
                                Log.Info("Smart Zoom (two-finger double-tap)");
                                _sink.Zoom(SmartZoomDelta);
                            } else if (_settings.TwoFingerTapMiddle && _tapCount == 0) {
                                // single two-finger tap -> middle click
                                _tapCount = 1;
                                _lastTapEndMs = now;
                                Log.Info("Two-finger tap -> middle click");
                                _sink.MouseButton(4);
                            } else {
                                _tapCount = 0;
                                _lastTapEndMs = 0;
                            }
                        } else {
                            _tapCount = 0;
                            _lastTapEndMs = 0;
                        }
                    } else {
                        // back to one finger: re-anchor so scroll does not jump
                        foreach (Tracked tr in _touches.Values) {
                            tr.AnchorX = tr.X;
                            tr.AnchorY = tr.Y;
                            tr.AccX = 0;
                            tr.AccY = 0;
                        }
                    }
                }

                if (_gestureMode) {
                    // Pinch detection (raw HID mode only — needs two finger
                    // positions; the Apple driver does not expose them over BT).
                    if (down >= 2 && _settings.PinchZoom && !_swipeFired) {
                        PinchStep();
                    }
                    if (!_swipeFired && !_pinchActive) {
                        int dx = CentroidX() - _gStartX;
                        int dy = CentroidY() - _gStartY;
                        GestureAction act = GestureAction.None;
                        if (Math.Abs(dx) >= GestureThreshold && Math.Abs(dx) > Math.Abs(dy) * 1.5) {
                            act = dx < 0 ? _settings.SwipeLeft : _settings.SwipeRight;
                        } else if (Math.Abs(dy) >= GestureThreshold && Math.Abs(dy) > Math.Abs(dx) * 1.5) {
                            act = dy < 0 ? _settings.SwipeUp : _settings.SwipeDown;
                        }
                        if (act != GestureAction.None) {
                            _swipeFired = true;
                            _tapCandidate = false;   // moved, not a tap
                            Log.Info("Gesture: " + GestureActionNames.Name(act) + " (dx=" + dx + ", dy=" + dy + ")");
                            _sink.GestureTriggered(act);
                        }
                    }
                } else if (down == 1) {
                    Tracked single = null;
                    foreach (Tracked t in _touches.Values) { single = t; break; }
                    if (single != null) ScrollStep(single);
                }
            }
        }

        void PinchStep() {
            // Distance between the two touches; spread = zoom in, close = zoom out.
            Tracked a = null, b = null;
            foreach (Tracked t in _touches.Values) {
                if (a == null) a = t; else { b = t; break; }
            }
            if (a == null || b == null) return;
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            double dist = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (!_pinchBaseSet) {
                _pinchBaseSet = true;
                _pinchBaseDist = dist;
                _pinchAcc = 0;
                return;
            }
            _pinchAcc += dist - _pinchBaseDist;
            _pinchBaseDist = dist;

            // Sensitivity 1-10 maps to 90..12 touch-units per zoom notch
            // (higher sensitivity = smaller step = zoom responds faster).
            int sens = Math.Max(1, Math.Min(10, _settings.PinchSensitivity));
            double step = 90 - sens * 7.8;           // sens5 -> 51, sens10 -> 12, sens1 -> 82
            if (step < 12) step = 12;
            int notches = (int)(_pinchAcc / step);
            if (notches != 0) {
                _pinchAcc -= notches * step;
                int dir = notches < 0 ? -1 : 1;
                int amount = Math.Abs(notches) * 120;
                _pinchActive = true;           // real pinch detected: don't also fire a swipe
                _tapCandidate = false;         // spread, not a tap
                Log.Info("Pinch: " + (dir > 0 ? "zoom in" : "zoom out") + " (" + amount + ")");
                _sink.Zoom(dir * amount);
            }
        }

        int TapZoneButton(int x) {
            // Surface is signed 12-bit (±SurfaceHalf). Left/right thirds -> left/right click,
            // middle third -> left click (unless the right-click zone is enabled).
            if (_settings.RightClickZone) {
                if (x > SurfaceHalf / 3) return 2;       // right third -> right click
            }
            return 1;                                     // everywhere else -> left click
        }

        int CentroidX() {
            int sum = 0, n = 0;
            foreach (Tracked t in _touches.Values) { sum += t.X; n++; }
            return n == 0 ? 0 : sum / n;
        }

        int CentroidY() {
            int sum = 0, n = 0;
            foreach (Tracked t in _touches.Values) { sum += t.Y; n++; }
            return n == 0 ? 0 : sum / n;
        }

        void ScrollStep(Tracked tr) {
            if (_settings.WindowsScrollPassthrough) return; // Windows handles scrolling itself
            int stepX = tr.AnchorX - tr.X;
            int stepY = tr.AnchorY - tr.Y;
            if (stepX == 0 && stepY == 0) return;

            int sens = Math.Max(1, Math.Min(10, _settings.Sensitivity));
            int scrollSpeed = (sens - 1) * 6;                    // 0..54  (kernel param semantics)
            double denom = (64 - scrollSpeed) * 7.0;             // ~448 (slow) .. 70 (fast)

            tr.AccX += stepX / denom;
            tr.AccY += stepY / denom;
            int wholeX = (int)tr.AccX;   // truncate toward zero, like the kernel
            int wholeY = (int)tr.AccY;
            bool emitX = _settings.HorizontalScroll && wholeX != 0;
            bool emitY = wholeY != 0;
            if (emitX) { tr.AccX -= wholeX; tr.AnchorX = tr.X; }
            if (emitY) { tr.AccY -= wholeY; tr.AnchorY = tr.Y; }

            if (!emitX && !emitY) return;

            // Direction. Kernel convention: finger up -> +wheel (natural). Windows
            // convention (NaturalScroll off) inverts Y. X follows the same flip.
            int dir = _settings.NaturalScroll ? 1 : -1;
            int scale = _settings.SmoothScroll ? 60 : 120; // WHEEL_DELTA granularity
            int dy = emitY ? wholeY * dir * scale : 0;
            int dx = emitX ? wholeX * dir * scale : 0;
            _sink.Scroll(dy, dx);
        }
    }
}
