using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MagicMouseEnhancer.Core;

namespace MagicMouseEnhancer {
    /// <summary>
    /// Low-level mouse hook (WH_MOUSE_LL). In driver mode the Apple kernel
    /// driver owns the raw touch data, so the app watches the standard mouse
    /// events the driver emits (WHEEL / HWHEEL / buttons) and:
    ///   - recognizes two-finger swipes (short HWHEEL bursts) -> Back/Forward,
    ///   - applies scroll sensitivity + natural-direction scaling,
    ///   - passes everything else through untouched.
    /// Runs on its own thread with a message pump (required by SetWindowsHookEx).
    /// </summary>
    public class MouseHook : IDisposable {
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT {
            public int ptX, ptY;
            public int mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX, ptY;
        }

        const int WH_MOUSE_LL = 14;
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_MOUSEHWHEEL = 0x020E;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;
        const uint PM_REMOVE = 0x0001;
        const int PumpSleepMs = 20;   // timer granularity for burst finalize

        /// <summary>Marker placed in dwExtraInfo of events this app injects, so
        /// the hook never processes its own output (no feedback loop).</summary>
        public static readonly IntPtr MagicMarker = new IntPtr(0x4D4D4501); // "MME\x01"

        readonly EventGestureEngine _engine;
        readonly ClickTapDetector _tapDetector;
        readonly object _stateLock = new object();
        LowLevelMouseProc _proc;
        IntPtr _hook = IntPtr.Zero;
        Thread _thread;
        volatile bool _running;
        volatile bool _enabled;

        // Deferred emission (we must NOT call SendInput from inside the hook
        // callback — reentrancy hazards — so queue it for the pump thread).
        readonly Queue<Action> _pending = new Queue<Action>();

        public MouseHook(EventGestureEngine engine) {
            _engine = engine;
            _tapDetector = new ClickTapDetector(engine.Settings);
        }

        public void SetEnabled(bool enabled) {
            _enabled = enabled;
        }

        public void Start() {
            lock (_stateLock) {
                if (_thread != null) return;
                _running = true;
                _proc = new LowLevelMouseProc(HookCallback);
                _thread = new Thread(Pump);
                _thread.IsBackground = true;
                _thread.Start();
            }
        }

        public void Stop() {
            lock (_stateLock) {
                _running = false;
                if (_hook != IntPtr.Zero) {
                    UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
                if (_thread != null) {
                    Thread t = _thread;
                    _thread = null;
                    if (!t.Join(1500)) t.Abort();
                }
                // release any held burst
                GestureAction act;
                List<int> rel;
                bool smartZoom;
                _engine.Flush(out act, out rel, out smartZoom);
                if (rel != null && rel.Count > 0) EmitDeltas(rel);
                if (smartZoom) EmitZoom();
                else if (act != GestureAction.None) EmitGesture(act);
            }
        }

        void Pump() {
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) {
                Log.Error("MouseHook: SetWindowsHookEx failed (win32 " + Marshal.GetLastWin32Error() + ")");
                _running = false;
                return;
            }
            Log.Info("MouseHook installed (WH_MOUSE_LL), pumping events");
            while (_running) {
                DrainPending();

                // Timer-driven burst finalization: a swipe ends with NO trailing
                // event, so we must close pending bursts on a schedule, not only
                // on the next event.
                if (_enabled) {
                    GestureAction act;
                    List<int> rel;
                    bool smartZoom;
                    if (_engine.CheckTimeout(out act, out rel, out smartZoom)) {
                        if (rel != null && rel.Count > 0) Queue(() => EmitDeltas(rel));
                        if (smartZoom) Queue(() => EmitZoom());
                        else if (act != GestureAction.None) Queue(() => EmitGesture(act));
                    }
                }

                MSG msg;
                while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE)) {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                Thread.Sleep(PumpSleepMs);
            }
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }

        void DrainPending() {
            while (true) {
                Action a;
                lock (_pending) {
                    if (_pending.Count == 0) return;
                    a = _pending.Dequeue();
                }
                try { a(); } catch { }
            }
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0 && _enabled) {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP) {
                    bool zoom;
                    bool swallow = _tapDetector.OnButton(msg == WM_LBUTTONDOWN, out zoom);
                    if (zoom) Queue(() => EmitZoom());
                    if (swallow) return (IntPtr)1;
                    return CallNextHookEx(_hook, nCode, wParam, lParam);
                }
                if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL) {
                    MSLLHOOKSTRUCT hs = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    // ignore our own injections
                    if (hs.dwExtraInfo != MagicMarker) {
                        int delta = (short)((hs.mouseData >> 16) & 0xFFFF);
                        if (msg == WM_MOUSEWHEEL) {
                            int scaled = _engine.HandleWheel(delta);
                            if (scaled == 0) return (IntPtr)1;            // swallow
                            if (scaled != delta) {
                                // rewrite the delta in place, then pass through
                                uint newData = unchecked((uint)((ushort)scaled << 16));
                                hs.mouseData = (int)((hs.mouseData & 0xFFFF) | newData);
                                Marshal.StructureToPtr(hs, lParam, true);
                            }
                        } else {
                            GestureAction act;
                            List<int> release;
                            bool smartZoom;
                            EventGestureEngine.HwDecision decision = _engine.HandleHWheel(delta, out act, out release, out smartZoom);
                            switch (decision) {
                                case EventGestureEngine.HwDecision.Swallow:
                                    return (IntPtr)1;                      // hold the event
                                case EventGestureEngine.HwDecision.Swipe:
                                    if (smartZoom) Queue(() => EmitZoom());
                                    else Queue(() => EmitGesture(act));
                                    return (IntPtr)1;                      // consume the swipe/tap
                                case EventGestureEngine.HwDecision.Scroll:
                                    if (release != null && release.Count > 0) Queue(() => EmitDeltas(release));
                                    return (IntPtr)1;                      // we re-emit below the hook
                            }
                        }
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        void Queue(Action a) {
            lock (_pending) _pending.Enqueue(a);
        }

        void EmitDeltas(List<int> deltas) {
            foreach (int d in deltas) NativeInput.SendWheel(0, d);
        }

        void EmitZoom() {
            Log.Info("Smart Zoom (two-finger double-tap)");
            NativeInput.SendZoom(EventGestureEngine.SmartZoomDelta);
        }

        void EmitGesture(GestureAction action) {
            Log.Info("Gesture (driver mode): " + GestureActionNames.Name(action));
            NativeInput.SendGestureAction(action);
        }

        public void Dispose() { Stop(); }
    }
}
