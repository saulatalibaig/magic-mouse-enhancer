using System;
using System.Collections.Generic;
using MagicMouseEnhancer.Core;

namespace MagicMouseEnhancer {
    /// <summary>
    /// Recognizes Magic Mouse gestures from the STANDARD mouse events that the
    /// Apple kernel driver emits (Bluetooth / driver mode). The driver converts
    /// the touch surface into ordinary wheel / horizontal-wheel events:
    ///
    ///   - one-finger vertical scroll     -> WHEEL  (continuous ±120 bursts)
    ///   - one-finger horizontal scroll   -> HWHEEL (continuous, long-lived)
    ///   - two-finger swipe left/right    -> HWHEEL (short burst: 3-6 notches
    ///     in under ~500 ms, then a pause)
    ///   - two-finger TAP                 -> HWHEEL (VERY short burst: 1-2
    ///     notches) — so a two-finger double-tap is two tiny bursts ~200-300 ms
    ///     apart -> Smart Zoom (Ctrl+wheel).
    ///
    /// Burst finalization is TIMER-DRIVEN: a held burst is finalized either by
    /// the next event (gap > BurstGapMs), a direction flip, exceeding the swipe
    /// limits (=> scroll), or by CheckTimeout() when the stream goes quiet
    /// (a swipe/tap ends with NO trailing event).
    ///
    /// The engine is pure (no input injection) so the self-test can drive it
    /// with synthetic event streams.
    /// </summary>
    public class EventGestureEngine {
        public enum HwDecision { Swallow, Swipe, Scroll }

        // -- burst classification constants (milliseconds / notches) ----------
        public const int SwipeMaxBurstMs = 500;      // a swipe is a short burst
        public const int SwipeMinNotches = 3;        // taps are 1-2 notches; swipes are 3+
        public const int SwipeMinDelta = 360;        // 3 x 120 = 360 minimum travel
        public const int MaxSwipeNotches = 6;        // more than this = scroll, not swipe
        public const int BurstGapMs = 120;           // gap longer than this splits a burst.
                                                     // Real swipes have 15-60ms inter-event gaps;
                                                     // a two-finger double-tap has a ~200ms hole
                                                     // in the middle, so a small gap threshold is
                                                     // what lets the engine SEE the double tap.
        public const int WHEEL_DELTA = 120;

        // -- smart zoom (two-finger double-tap) --------------------------------
        public const int SmartZoomTapWindowMs = 400; // max gap between the two tiny bursts
        public const int SmartZoomMaxTotal = 480;    // each tap burst is at most ~4 notches
        public const int SmartZoomDelta = 240;       // zoom in two notches per double-tap

        readonly Settings _settings;
        readonly object _sync = new object();

        // candidate burst state (one direction at a time)
        bool _cand;
        int _candDir;
        long _candFirst;
        long _candLast;
        int _candCount;
        int _candTotal;
        long _fakeClock; // 0 = use real clock (self-test sets this)

        // previous small-burst info, for double-tap detection
        long _lastBurstEndMs;
        int _lastBurstTotal;

        public EventGestureEngine(Settings settings) {
            _settings = settings;
        }

        public Settings Settings { get { return _settings; } }

        // Test hook: a non-zero value overrides Environment.TickCount.
        public void SetClock(long ms) { _fakeClock = ms; }

        long NowMs() {
            if (_fakeClock != 0) return _fakeClock;
            return Environment.TickCount & 0x7FFFFFFF;
        }

        /// <summary>Vertical wheel: returns the (possibly transformed) delta to
        /// emit, or 0 to swallow. Sensitivity + natural direction apply.</summary>
        public int HandleWheel(int delta) {
            lock (_sync) {
                if (!_settings.Enabled || _settings.WindowsScrollPassthrough) return delta;
                return Scale(delta);
            }
        }

        /// <summary>Horizontal wheel: classifies the burst. Swallow = hold the
        /// event; Swipe = burst qualified (or a double-tap Smart Zoom), the
        /// action/zoom to fire is reported; Scroll = burst did not qualify,
        /// emit releaseDeltas as a scroll.</summary>
        public HwDecision HandleHWheel(int delta, out GestureAction swipeAction, out List<int> releaseDeltas,
                                       out bool smartZoom) {
            swipeAction = GestureAction.None;
            releaseDeltas = null;
            smartZoom = false;
            lock (_sync) {
                if (!_settings.Enabled || _settings.WindowsScrollPassthrough
                    || !_settings.SwipeFromHorizontalWheel) {
                    releaseDeltas = new List<int> { delta };
                    return HwDecision.Scroll;
                }
                int dir = Math.Sign(delta);
                long now = NowMs();

                if (!_cand) {
                    StartBurst(delta, dir, now);
                    return HwDecision.Swallow;      // hold first event, watch for more
                }

                if (dir != _candDir) {
                    // direction flipped: finalize old burst, start a new one
                    List<int> rel = null;
                    GestureAction act = FinalizeBurst(out rel, out smartZoom);
                    StartBurst(delta, dir, now);
                    if (smartZoom) return HwDecision.Swipe;
                    if (act != GestureAction.None) { swipeAction = act; return HwDecision.Swipe; }
                    releaseDeltas = rel;
                    return HwDecision.Scroll;
                }

                long gap = now - _candLast;
                if (gap > BurstGapMs) {
                    // pause long enough: old burst is over
                    List<int> rel = null;
                    GestureAction act = FinalizeBurst(out rel, out smartZoom);
                    StartBurst(delta, dir, now);
                    if (smartZoom) return HwDecision.Swipe;
                    if (act != GestureAction.None) { swipeAction = act; return HwDecision.Swipe; }
                    releaseDeltas = rel;
                    return HwDecision.Scroll;
                }

                // same direction, still within the burst window
                _candLast = now;
                _candCount++;
                _candTotal += delta;

                if (_candCount > MaxSwipeNotches || (now - _candFirst) > SwipeMaxBurstMs) {
                    // too many / too long: this is a scroll, not a swipe.
                    // Release everything held so far + this event as a scroll.
                    List<int> rel = new List<int>();
                    rel.Add(_candTotal - delta);    // the held burst
                    rel.Add(delta);                 // the current event
                    releaseDeltas = TransformDeltas(rel);
                    _cand = false;
                    return HwDecision.Scroll;
                }

                return HwDecision.Swallow;          // keep holding
            }
        }

        /// <summary>Timer-driven finalize: if a burst has been silent longer than
        /// BurstGapMs, close it now (a swipe/tap ends with no trailing event).
        /// Returns true if anything was pending; the swipe action, the scroll
        /// deltas to release, and/or a Smart Zoom are filled in.</summary>
        public bool CheckTimeout(out GestureAction swipeAction, out List<int> releaseDeltas, out bool smartZoom) {
            swipeAction = GestureAction.None;
            releaseDeltas = null;
            smartZoom = false;
            lock (_sync) {
                if (!_cand) return false;
                if (NowMs() - _candLast <= BurstGapMs) return false;
                List<int> rel = null;
                swipeAction = FinalizeBurst(out rel, out smartZoom);
                releaseDeltas = rel;
                return true;
            }
        }

        /// <summary>Force-finish any in-flight burst (called on hook stop).</summary>
        public void Flush(out GestureAction swipeAction, out List<int> releaseDeltas, out bool smartZoom) {
            swipeAction = GestureAction.None;
            releaseDeltas = null;
            smartZoom = false;
            lock (_sync) {
                if (!_cand) return;
                List<int> rel = null;
                swipeAction = FinalizeBurst(out rel, out smartZoom);
                releaseDeltas = rel;
            }
        }

        void StartBurst(int delta, int dir, long now) {
            _cand = true;
            _candDir = dir;
            _candFirst = now;
            _candLast = now;
            _candCount = 1;
            _candTotal = delta;
        }

        /// <summary>Called when a burst ends. If it qualifies as a swipe the
        /// buffered events are discarded; otherwise they are returned as a
        /// scroll (already transformed). If this tiny burst is the second one
        /// within the Smart Zoom window, reports smartZoom instead.</summary>
        GestureAction FinalizeBurst(out List<int> releaseDeltas, out bool smartZoom) {
            releaseDeltas = null;
            smartZoom = false;

            bool swipe = _candCount >= SwipeMinNotches
                      && Math.Abs(_candTotal) >= SwipeMinDelta
                      && (_candLast - _candFirst) <= SwipeMaxBurstMs;

            // Two-finger double-tap: the Apple driver reports a tap as a tiny
            // HWHEEL burst. Two small bursts (each <= SmartZoomMaxTotal) ending
            // within SmartZoomTapWindowMs of each other = double-tap -> zoom.
            if (_settings.SmartZoom && _lastBurstEndMs != 0
                && (_candLast - _lastBurstEndMs) <= SmartZoomTapWindowMs
                && Math.Abs(_candTotal) <= SmartZoomMaxTotal
                && Math.Abs(_lastBurstTotal) <= SmartZoomMaxTotal) {
                _cand = false;
                _lastBurstEndMs = 0;
                _lastBurstTotal = 0;
                smartZoom = true;
                Log.Info("Burst " + _candCount + "x" + _candTotal + " -> Smart Zoom (double-tap)");
                return GestureAction.None;
            }

            _cand = false;
            _lastBurstEndMs = _candLast;
            _lastBurstTotal = _candTotal;

            if (!swipe) {
                releaseDeltas = TransformDeltas(new List<int> { _candTotal });
                Log.Info("Burst " + _candCount + "x" + _candTotal + " -> scroll");
                return GestureAction.None;
            }
            int dir = _candDir;
            Log.Info("Burst " + _candCount + "x" + _candTotal + " -> swipe " + (dir < 0 ? "L" : "R"));
            return dir < 0 ? _settings.SwipeLeft : _settings.SwipeRight;
        }

        int Scale(int delta) {
            int d = _settings.NaturalScroll ? -delta : delta;
            double factor = _settings.Sensitivity / 5.0;   // 1 -> 0.2x, 5 -> 1x, 10 -> 2x
            int scaled = (int)Math.Round(d * factor);
            if (scaled > 480) scaled = 480;
            if (scaled < -480) scaled = -480;
            return scaled;
        }

        List<int> TransformDeltas(List<int> deltas) {
            List<int> result = new List<int>();
            foreach (int d in deltas) {
                int scaled = Scale(d);
                if (scaled != 0) result.Add(scaled);
            }
            return result;
        }
    }
}
