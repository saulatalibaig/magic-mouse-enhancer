using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MagicMouseEnhancer.Core;
using MagicMouseEnhancer.Hid;

namespace MagicMouseEnhancer {
    /// <summary>
    /// Offline verification suite. Run with:  MagicMouseEnhancer.exe --selftest
    /// Exercises the decoder against hand-computed HID report vectors, the gesture
    /// engine's scroll/swipe math, settings persistence and the HID enumeration API.
    /// Exits 0 on success, 1 on failure; writes selftest-results.txt next to the log.
    /// </summary>
    public static class SelfTest {
        const string TestRegKey = @"Software\MagicMouseEnhancer\SelfTest";

        static int _failures;
        static readonly List<string> _lines = new List<string>();

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [StructLayout(LayoutKind.Sequential)]
        struct OSVERSIONINFOEX {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll")]
        static extern int RtlGetVersion(ref OSVERSIONINFOEX info);

        static string OsVersion() {
            try {
                OSVERSIONINFOEX v = new OSVERSIONINFOEX();
                v.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                if (RtlGetVersion(ref v) == 0) {
                    return string.Format("{0}.{1}.{2} (build {3})", v.dwMajorVersion, v.dwMinorVersion, v.dwBuildNumber, v.dwBuildNumber);
                }
            } catch { }
            return Environment.OSVersion.VersionString;
        }

        public static int Run() {
            AttachConsole(-1); // attach to parent console (PowerShell) if present
            try {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            } catch { }

            Log.Init();
            _lines.Add("Magic Mouse Enhancer self-test — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _lines.Add("Platform: Windows " + OsVersion() + " (" + (Environment.Is64BitProcess ? "x64" : "x86") + " process)");

            Run("Decode MM1 two-touch report", TestDecodeMm1);
            Run("Decode MM2 touch report", TestDecodeMm2);
            Run("Decode MM2 plain mouse report", TestDecodeMm2Plain);
            Run("Decode rejects malformed reports", TestDecodeMalformed);
            Run("One-finger scroll emits wheel (Windows direction)", TestScrollWindowsDirection);
            Run("Natural scrolling inverts wheel", TestNaturalScroll);
            Run("Horizontal scroll emits HWHEEL", TestHorizontalScroll);
            Run("Scroll sensitivity affects notch rate", TestSensitivity);
            Run("Two-finger swipe left/right fires gestures", TestSwipeGestures);
            Run("Two-finger swipe fires vertical gestures", TestSwipeVertical);
            Run("Swipe does not fire for small movement", TestNoFalseSwipe);
            Run("Settings registry round-trip", TestSettingsRoundTrip);
            Run("HID enumeration API", TestHidEnum);
            Run("Input structure marshaling", TestInputLayout);
            Run("Event swipe: short HWHEEL burst -> Back", TestEventSwipeLeft);
            Run("Event swipe: short HWHEEL burst -> Forward", TestEventSwipeRight);
            Run("Event swipe: continuous HWHEEL stays scroll", TestEventContinuousScroll);
            Run("Event swipe: single notch stays scroll", TestEventSingleNotch);
            Run("Event wheel: sensitivity scales deltas", TestEventSensitivity);
            Run("Event wheel: natural scroll inverts", TestEventNatural);
            Run("Event engine: disabled = passthrough", TestEventDisabled);
            Run("Event double-tap: click pair zooms", TestClickTapDoubleClick);
            Run("Event double-tap: slow clicks do not zoom", TestClickTapSlowClicks);
            Run("Event double-tap: single click does not zoom", TestClickTapSingle);
            Run("Pinch: spreading fingers zooms in", TestPinchZoomIn);
            Run("Pinch: closing fingers zooms out", TestPinchZoomOut);
            Run("Pinch: disabled by setting", TestPinchDisabled);
            Run("Pinch sensitivity: 10 zooms more than 1", TestPinchSensitivity);
            Run("Smart zoom: two-finger double-tap zooms", TestSmartZoomDoubleTap);
            Run("Smart zoom: single tap does not zoom", TestSmartZoomSingleTap);
            Run("Smart zoom: slow taps do not zoom", TestSmartZoomSlowTaps);
            Run("Tap-to-click: quick tap clicks", TestTapToClick);
            Run("Tap-to-click: moving finger does not click", TestTapToClickMove);
            Run("Two-finger tap = middle click", TestTwoFingerMiddle);
            Run("Actions: virtual desktop + browser nav", TestNewActions);

            string summary = string.Format("PASS: {0}   FAIL: {1}", _lines.Count - 1 - _failures, _failures);
            _lines.Add("RESULT: " + summary);
            Emit(summary);

            try {
                string dir = Path.GetDirectoryName(Log.LogPath);
                if (string.IsNullOrEmpty(dir)) dir = ".";
                File.WriteAllLines(Path.Combine(dir, "selftest-results.txt"), _lines.ToArray(), Encoding.UTF8);
                Emit("Details written to " + Path.Combine(dir, "selftest-results.txt"));
            } catch (Exception ex) {
                Emit("Could not write results file: " + ex.Message);
            }
            return _failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- harness

        static void Run(string name, Func<string> test) {
            try {
                string problem = test();
                bool ok = problem == null;
                string line = (ok ? "PASS  " : "FAIL  ") + name + (ok ? "" : "  —  " + problem);
                _lines.Add(line);
                Emit(line);
                if (!ok) _failures++;
            } catch (Exception ex) {
                string line = "FAIL  " + name + "  —  EXCEPTION: " + ex.GetType().Name + ": " + ex.Message;
                _lines.Add(line);
                Emit(line);
                _failures++;
            }
        }

        static void Emit(string line) {
            try { Console.WriteLine(line); } catch { }
        }

        static string Check(bool cond, string what) { return cond ? null : what; }

        // ---------------------------------------------------------------- decoder

        static string TestDecodeMm1() {
            // MM1: report id 0x29, 6-byte prefix, then N*8 touch bytes.
            // touch1 (hand-computed): id=3, x=100, y=200 (kernel sign), size=20, major=40, minor=30, state=0x30
            //   x = (t1<<28 | t0<<20)>>20 ; y = -((t2<<24|t1<<16)>>20)  (t1 low nibble -> x, high nibble -> y)
            //   x=100 -> t0=0x64, t1 low=0 ; y_kernel=200 -> y12=-200=0xF38 -> t2=0xF3, t1 high=8 -> t1=0x80
            // touch2: id=5, x=-50, y=-75, size=15, major=32, minor=16, state=0x40
            //   x=-50 -> x12=0xFCE -> t0=0xCE, t1 low=F ; y_kernel=-75 -> y12=0x04B -> t2=0x04, t1 high=B -> t1=0xBF
            byte[] rep = new byte[] {
                0x29, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x64, 0x80, 0xF3, 0x28, 0x1E, 0xD4, 0x00, 0x30,
                0xCE, 0xBF, 0x04, 0x20, 0x10, 0x4F, 0x01, 0x40
            };
            TouchReport r = TouchDecoder.Decode(rep, rep.Length);
            if (r == null) return "decoder returned null";
            if (r.ReportId != 0x29) return "wrong report id";
            if (r.Touches.Count != 2) return "expected 2 touches, got " + r.Touches.Count;
            TouchPoint t0 = r.Touches[0];
            TouchPoint t1 = r.Touches[1];
            return Check(t0.Id == 3 && t0.X == 100 && t0.Y == 200 && t0.Size == 20 &&
                         t0.TouchMajor == 40 && t0.TouchMinor == 30 && t0.RawState == 0x30 && t0.Down,
                "touch0 mismatch: id=" + t0.Id + " x=" + t0.X + " y=" + t0.Y + " size=" + t0.Size +
                " major=" + t0.TouchMajor + " minor=" + t0.TouchMinor + " state=0x" + t0.RawState.ToString("X2"))
            ?? Check(t1.Id == 5 && t1.X == -50 && t1.Y == -75 && t1.Size == 15 &&
                     t1.RawState == 0x40 && t1.Down,
                "touch1 mismatch: id=" + t1.Id + " x=" + t1.X + " y=" + t1.Y + " size=" + t1.Size +
                " state=0x" + t1.RawState.ToString("X2"))
            ?? Check(r.Clicks == 0 && r.RelX == 0 && r.RelY == 0, "prefix decode mismatch");
        }

        static string TestDecodeMm2() {
            // MM2: report id 0x12, 14-byte prefix + 1*8 touch bytes.
            // x=7, y=-3 (from prefix), clicks=1; touch: id=1, x=33, y=44, size=12, state=0x40
            //   x=33 -> t0=0x21, t1 low=0 ; y_kernel=44 -> y12=-44=0xFD4 -> t2=0xFD, t1 high=4 -> t1=0x40
            byte[] rep = new byte[] {
                0x12, 0x01, 0x07, 0x00, 0xFD, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x21, 0x40, 0xFD, 0x0A, 0x09, 0x4C, 0x00, 0x40
            };
            TouchReport r = TouchDecoder.Decode(rep, rep.Length);
            if (r == null) return "decoder returned null";
            if (r.Touches.Count != 1) return "expected 1 touch, got " + r.Touches.Count;
            TouchPoint t = r.Touches[0];
            return Check(r.Clicks == 1 && r.RelX == 7 && r.RelY == -3, "prefix mismatch: clicks=" + r.Clicks + " x=" + r.RelX + " y=" + r.RelY)
            ?? Check(t.Id == 1 && t.X == 33 && t.Y == 44 && t.Size == 12 && t.RawState == 0x40 && t.Down,
                "touch mismatch: id=" + t.Id + " x=" + t.X + " y=" + t.Y + " size=" + t.Size);
        }

        static string TestDecodeMm2Plain() {
            byte[] rep = new byte[] { 0x12, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            TouchReport r = TouchDecoder.Decode(rep, rep.Length);
            if (r == null) return "decoder returned null";
            return Check(r.Clicks == 2 && r.Touches.Count == 0, "plain MM2 report mismatch: clicks=" + r.Clicks + " touches=" + r.Touches.Count);
        }

        static string TestDecodeMalformed() {
            byte[] tooShort = new byte[] { 0x29, 0x01 };                       // MM1 needs >= 6 bytes
            byte[] wrongSize = new byte[] { 0x12, 0, 0, 0, 0, 0, 0, 0, 0 };     // MM2 must be 8 or 14+8N
            byte[] wrongId = new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0 };          // not a Magic Mouse report
            return Check(TouchDecoder.Decode(tooShort, tooShort.Length) == null, "tooShort should be rejected")
            ?? Check(TouchDecoder.Decode(wrongSize, wrongSize.Length) == null, "wrongSize should be rejected")
            ?? Check(TouchDecoder.Decode(wrongId, wrongId.Length) == null, "wrongId should be rejected")
            ?? Check(TouchDecoder.Decode(null, 0) == null, "null should be rejected");
        }

        // ---------------------------------------------------------------- engine

        class TestSink : GestureEngine.ISink {
            public readonly List<int> WheelY = new List<int>();
            public readonly List<int> WheelX = new List<int>();
            public readonly List<GestureAction> Actions = new List<GestureAction>();
            public readonly List<int> Zooms = new List<int>();
            public readonly List<int> Buttons = new List<int>();
            public void Scroll(int deltaY, int deltaX) {
                if (deltaY != 0) WheelY.Add(deltaY);
                if (deltaX != 0) WheelX.Add(deltaX);
            }
            public void GestureTriggered(GestureAction action) { Actions.Add(action); }
            public void Zoom(int wheelDelta) { Zooms.Add(wheelDelta); }
            public void MouseButton(int button) { Buttons.Add(button); }
        }

        static GestureEngine NewEngine(TestSink sink, Settings settings) {
            return new GestureEngine(sink, settings);
        }

        static void Feed(GestureEngine engine, int id, int x, int y, byte state) {
            TouchReport r = new TouchReport();
            TouchPoint p = new TouchPoint();
            p.Id = id;
            p.X = x;
            p.Y = y;
            p.RawState = state;
            p.Down = state != TouchDecoder.STATE_NONE;
            r.Touches.Add(p);
            engine.Process(r);
        }

        /// <summary>Feed a report with both touches present — real HID reports always carry all active touches.</summary>
        static void Feed2(GestureEngine engine, int id1, int x1, int y1, byte s1, int id2, int x2, int y2, byte s2) {
            TouchReport r = new TouchReport();
            TouchPoint p1 = new TouchPoint();
            p1.Id = id1; p1.X = x1; p1.Y = y1; p1.RawState = s1; p1.Down = s1 != TouchDecoder.STATE_NONE;
            r.Touches.Add(p1);
            TouchPoint p2 = new TouchPoint();
            p2.Id = id2; p2.X = x2; p2.Y = y2; p2.RawState = s2; p2.Down = s2 != TouchDecoder.STATE_NONE;
            r.Touches.Add(p2);
            engine.Process(r);
        }

        static Settings DefaultTestSettings() {
            Settings s = Settings.LoadFrom(TestRegKey);
            s.Enabled = true;
            s.NaturalScroll = false;
            s.Sensitivity = 5;
            s.HorizontalScroll = true;
            s.SmoothScroll = false;
            s.WindowsScrollPassthrough = false;
            return s;
        }

        static string TestScrollWindowsDirection() {
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, DefaultTestSettings());
            // sensitivity 5 -> scrollSpeed 24 -> denom (64-24)*7 = 280
            // finger moves UP 300 units: stepY = 0-300 = -300 -> wholeY = -1
            // Windows direction (natural off): emit dy = wholeY * -1 * 120 = +120 (scroll up)
            Feed(e, 1, 0, 0, TouchDecoder.STATE_START);
            Feed(e, 1, 0, 300, TouchDecoder.STATE_DRAG);
            return Check(sink.WheelY.Count == 1 && sink.WheelY[0] == 120, "expected +120, got " + SinkDump(sink))
            ?? Check(sink.WheelX.Count == 0, "unexpected horizontal: " + SinkDump(sink));
        }

        static string TestNaturalScroll() {
            TestSink sink = new TestSink();
            Settings s = DefaultTestSettings();
            s.NaturalScroll = true;
            GestureEngine e = NewEngine(sink, s);
            Feed(e, 1, 0, 0, TouchDecoder.STATE_START);
            Feed(e, 1, 0, 300, TouchDecoder.STATE_DRAG);
            return Check(sink.WheelY.Count == 1 && sink.WheelY[0] == -120, "natural expected -120, got " + SinkDump(sink));
        }

        static string TestHorizontalScroll() {
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, DefaultTestSettings());
            Feed(e, 1, 0, 0, TouchDecoder.STATE_START);
            Feed(e, 1, 400, 0, TouchDecoder.STATE_DRAG);   // finger right 400 -> wholeX = -1 -> +120 (right)
            return Check(sink.WheelX.Count == 1 && sink.WheelX[0] == 120, "expected +120 horizontal, got " + SinkDump(sink))
            ?? Check(sink.WheelY.Count == 0, "unexpected vertical: " + SinkDump(sink));
        }

        static string TestSensitivity() {
            // slow (sens 1): denom (64-0)*7 = 448 -> 300 units = 0.67 notches -> no emit
            TestSink slow = new TestSink();
            Settings s1 = DefaultTestSettings();
            s1.Sensitivity = 1;
            GestureEngine e1 = NewEngine(slow, s1);
            Feed(e1, 1, 0, 0, TouchDecoder.STATE_START);
            Feed(e1, 1, 0, 300, TouchDecoder.STATE_DRAG);
            if (slow.WheelY.Count != 0) return "sens=1 should not emit yet, got " + SinkDump(slow);
            // fast (sens 10): scrollSpeed 54 -> denom (64-54)*7 = 70 -> 300 units = 4.3 notches
            TestSink fast = new TestSink();
            Settings s2 = DefaultTestSettings();
            s2.Sensitivity = 10;
            GestureEngine e2 = NewEngine(fast, s2);
            Feed(e2, 1, 0, 0, TouchDecoder.STATE_START);
            Feed(e2, 1, 0, 300, TouchDecoder.STATE_DRAG);
            return Check(fast.WheelY.Count == 1 && fast.WheelY[0] > 0 && fast.WheelY[0] >= 4 * 120,
                "sens=10 should emit >=4 notches, got " + SinkDump(fast));
        }

        static string TestSwipeGestures() {
            TestSink sink = new TestSink();
            Settings s = DefaultTestSettings();
            s.SwipeLeft = GestureAction.Back;
            s.SwipeRight = GestureAction.Forward;
            GestureEngine e = NewEngine(sink, s);

            // two fingers down, then both move left 700 units
            Feed2(e, 1, 0, 0, TouchDecoder.STATE_START, 2, 0, 60, TouchDecoder.STATE_START);
            Feed2(e, 1, -700, 0, TouchDecoder.STATE_DRAG, 2, -700, 60, TouchDecoder.STATE_DRAG);
            string p1 = Check(sink.Actions.Count == 1 && sink.Actions[0] == GestureAction.Back,
                "expected Back once, got " + ActionsDump(sink))
            ?? Check(sink.WheelY.Count == 0 && sink.WheelX.Count == 0, "gesture must not scroll: " + SinkDump(sink));
            if (p1 != null) return p1;

            // release both, then swipe right
            Feed2(e, 1, -700, 0, 0x00, 2, -700, 60, 0x00);
            Feed2(e, 1, 0, 0, TouchDecoder.STATE_START, 2, 0, 60, TouchDecoder.STATE_START);
            Feed2(e, 1, 700, 0, TouchDecoder.STATE_DRAG, 2, 700, 60, TouchDecoder.STATE_DRAG);
            return Check(sink.Actions.Count == 2 && sink.Actions[1] == GestureAction.Forward,
                "expected Forward second, got " + ActionsDump(sink));
        }

        static string TestSwipeVertical() {
            TestSink sink = new TestSink();
            Settings s = DefaultTestSettings();
            s.SwipeUp = GestureAction.TaskView;
            s.SwipeDown = GestureAction.ShowDesktop;
            GestureEngine e = NewEngine(sink, s);
            Feed2(e, 1, 0, 0, TouchDecoder.STATE_START, 2, 60, 0, TouchDecoder.STATE_START);
            Feed2(e, 1, 0, -700, TouchDecoder.STATE_DRAG, 2, 60, -700, TouchDecoder.STATE_DRAG);   // up
            string p1 = Check(sink.Actions.Count == 1 && sink.Actions[0] == GestureAction.TaskView,
                "expected TaskView, got " + ActionsDump(sink));
            if (p1 != null) return p1;
            Feed2(e, 1, 0, -700, 0x00, 2, 60, -700, 0x00);
            Feed2(e, 1, 0, 0, TouchDecoder.STATE_START, 2, 60, 0, TouchDecoder.STATE_START);
            Feed2(e, 1, 0, 700, TouchDecoder.STATE_DRAG, 2, 60, 700, TouchDecoder.STATE_DRAG);     // down
            return Check(sink.Actions.Count == 2 && sink.Actions[1] == GestureAction.ShowDesktop,
                "expected ShowDesktop, got " + ActionsDump(sink));
        }

        static string TestNoFalseSwipe() {
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, DefaultTestSettings());
            Feed2(e, 1, 0, 0, TouchDecoder.STATE_START, 2, 0, 60, TouchDecoder.STATE_START);
            Feed2(e, 1, -100, 0, TouchDecoder.STATE_DRAG, 2, -100, 60, TouchDecoder.STATE_DRAG);   // below threshold
            return Check(sink.Actions.Count == 0, "small movement must not fire a gesture: " + ActionsDump(sink));
        }

        static string SinkDump(TestSink sink) {
            return "Y=[" + string.Join(",", sink.WheelY.ToArray()) + "] X=[" + string.Join(",", sink.WheelX.ToArray()) + "]";
        }

        static string ActionsDump(TestSink sink) {
            StringBuilder sb = new StringBuilder();
            foreach (GestureAction a in sink.Actions) sb.Append(a.ToString()).Append(' ');
            return sb.ToString().Trim();
        }

        // ---------------------------------------------------------------- settings

        static string TestSettingsRoundTrip() {
            try {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(TestRegKey, false);
            } catch { }
            Settings s = Settings.LoadFrom(TestRegKey);
            s.Enabled = false;
            s.NaturalScroll = true;
            s.Sensitivity = 9;
            s.HorizontalScroll = false;
            s.SmoothScroll = true;
            s.WindowsScrollPassthrough = true;
            s.SwipeLeft = GestureAction.Home;
            s.SwipeRight = GestureAction.End;
            s.SwipeUp = GestureAction.None;
            s.SwipeDown = GestureAction.PageDown;
            s.AutoStart = true;
            s.Save();

            Settings t = Settings.LoadFrom(TestRegKey);
            string p = Check(t.Enabled == false && t.NaturalScroll && t.Sensitivity == 9 && !t.HorizontalScroll &&
                             t.SmoothScroll && t.WindowsScrollPassthrough && t.AutoStart,
                "boolean/numeric round-trip failed: sens=" + t.Sensitivity + " natural=" + t.NaturalScroll)
            ?? Check(t.SwipeLeft == GestureAction.Home && t.SwipeRight == GestureAction.End &&
                     t.SwipeUp == GestureAction.None && t.SwipeDown == GestureAction.PageDown,
                "action round-trip failed: " + t.SwipeLeft + "," + t.SwipeRight + "," + t.SwipeUp + "," + t.SwipeDown);

            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(TestRegKey, false); } catch { }
            return p;
        }

        // ---------------------------------------------------------------- system

        static string TestHidEnum() {
            List<string> paths = HidApi.EnumerateHidDevicePaths();
            if (paths == null) return "enumeration returned null";
            int appleMice = 0;
            foreach (string p in paths) {
                ushort vid, pid;
                if (!TryReadAttributes(p, out vid, out pid)) continue;
                if (MagicMouse.IsMagicMouse(vid, pid)) appleMice++;
            }
            Emit("      HID interfaces present: " + paths.Count + ", Magic Mice found: " + appleMice +
                 (appleMice == 0 ? " (expected on this machine — mouse not paired)" : ""));
            return null; // enumeration working is the assertion; device presence is environment-dependent
        }

        static bool TryReadAttributes(string path, out ushort vid, out ushort pid) {
            vid = 0; pid = 0;
            IntPtr h = HidApi.CreateFile(path, HidApi.GENERIC_READ,
                HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            if (HidApi.IsInvalidHandle(h)) return false;
            try {
                HidApi.HIDD_ATTRIBUTES a = new HidApi.HIDD_ATTRIBUTES();
                a.Size = 10;
                if (!HidApi.HidD_GetAttributes(h, ref a)) return false;
                vid = a.VendorID;
                pid = a.ProductID;
                return true;
            } finally { HidApi.CloseHandle(h); }
        }

        static string TestInputLayout() {
            int expectedInput = IntPtr.Size == 8 ? 40 : 28;
            int actualInput = Marshal.SizeOf(typeof(NativeInput.INPUT));
            // MOUSEINPUT: 5 DWORDs + ULONG_PTR, aligned -> 32 bytes on x64, 24 on x86
            int expectedMouse = IntPtr.Size == 8 ? 32 : 24;
            int actualMouse = Marshal.SizeOf(typeof(NativeInput.MOUSEINPUT));
            return Check(actualInput == expectedInput, "INPUT marshals to " + actualInput + " bytes, expected " + expectedInput)
            ?? Check(actualMouse == expectedMouse, "MOUSEINPUT marshals to " + actualMouse + " bytes, expected " + expectedMouse);
        }

        // ------------------------------------------------- event gesture engine

        static EventGestureEngine NewEventEngine() {
            Settings s = new Settings();
            s.Enabled = true;
            s.WindowsScrollPassthrough = false;
            s.SwipeFromHorizontalWheel = true;
            s.NaturalScroll = false;
            s.Sensitivity = 5;
            return new EventGestureEngine(s);
        }

        static string TestEventSwipeLeft() {
            // short burst: 3 x -120 within 200 ms, then SILENCE (a swipe ends with
            // no trailing event) -> the timer-driven CheckTimeout must fire Back
            EventGestureEngine e = NewEventEngine();
            long t = 1000;
            GestureAction act; List<int> rel; bool sz;
            e.SetClock(t); if (e.HandleHWheel(-120, out act, out rel, out sz) != EventGestureEngine.HwDecision.Swallow) return "first notch not swallowed";
            e.SetClock(t += 60); if (e.HandleHWheel(-120, out act, out rel, out sz) != EventGestureEngine.HwDecision.Swallow) return "second notch not swallowed";
            e.SetClock(t += 60); if (e.HandleHWheel(-120, out act, out rel, out sz) != EventGestureEngine.HwDecision.Swallow) return "third notch not swallowed";
            // no more events: after BurstGapMs of silence the timer fires
            e.SetClock(t += 400);
            if (!e.CheckTimeout(out act, out rel, out sz)) return "CheckTimeout did not fire";
            return Check(sz == false && act == GestureAction.Back, "expected Back, got act=" + act + " sz=" + sz)
            ?? Check(rel == null || rel.Count == 0, "swipe should not release scroll deltas");
        }

        static string TestEventSwipeRight() {
            EventGestureEngine e = NewEventEngine();
            long t = 1000;
            GestureAction act; List<int> rel; bool sz;
            e.SetClock(t); e.HandleHWheel(120, out act, out rel, out sz);
            e.SetClock(t += 60); e.HandleHWheel(120, out act, out rel, out sz);
            e.SetClock(t += 60); e.HandleHWheel(120, out act, out rel, out sz);
            e.SetClock(t += 400);
            if (!e.CheckTimeout(out act, out rel, out sz)) return "CheckTimeout did not fire";
            return Check(sz == false && act == GestureAction.Forward, "expected Forward, got act=" + act + " sz=" + sz);
        }

        static string TestEventContinuousScroll() {
            // continuous long stream (every 80 ms): must be released as scroll
            // (either via the too-many/too-long branch or the pause), never swipe
            EventGestureEngine e = NewEventEngine();
            long t = 1000;
            GestureAction act; List<int> rel; bool sz;
            bool sawScroll = false;
            for (int i = 0; i < 20; i++) {
                e.SetClock(t += 80);
                EventGestureEngine.HwDecision d = e.HandleHWheel(120, out act, out rel, out sz);
                if (d == EventGestureEngine.HwDecision.Scroll) sawScroll = true;
                if (act != GestureAction.None || sz) return "swipe/zoom fired during continuous scroll";
            }
            // long burst exceeds SwipeMaxBurstMs -> must already have released as scroll
            return Check(sawScroll, "continuous horizontal scroll never released as scroll");
        }

        static string TestEventSingleNotch() {
            EventGestureEngine e = NewEventEngine();
            long t = 1000;
            GestureAction act; List<int> rel; bool sz;
            e.SetClock(t); EventGestureEngine.HwDecision d = e.HandleHWheel(-120, out act, out rel, out sz);
            // one notch then silence -> CheckTimeout must release it as scroll
            e.SetClock(t + 800);
            if (!e.CheckTimeout(out act, out rel, out sz)) return "CheckTimeout did not fire";
            return Check(sz == false && act == GestureAction.None && rel != null && rel.Count == 1,
                "single notch did not release as scroll");
        }

        static string TestClickTapDoubleClick() {
            // The Apple driver maps a two-finger double-tap to a double-click:
            // L_DOWN/L_UP, then L_DOWN/L_UP ~250 ms later -> Smart Zoom.
            Settings s = DefaultTestSettings();
            s.SmartZoom = true;
            ClickTapDetector d = new ClickTapDetector(s);
            long t = 1000;
            bool zoom;
            d.SetClock(t); if (d.OnButton(true, out zoom) || zoom) return "first press should pass";
            d.SetClock(t += 60); if (d.OnButton(false, out zoom) || zoom) return "first release should pass";
            d.SetClock(t += 250); bool swallow = d.OnButton(true, out zoom);
            return Check(swallow && zoom, "second press should swallow + zoom");
        }

        static string TestClickTapSlowClicks() {
            Settings s = DefaultTestSettings();
            s.SmartZoom = true;
            ClickTapDetector d = new ClickTapDetector(s);
            long t = 1000;
            bool zoom;
            d.SetClock(t); d.OnButton(true, out zoom);
            d.SetClock(t += 60); d.OnButton(false, out zoom);
            d.SetClock(t += 900); bool swallow = d.OnButton(true, out zoom);   // too slow
            return Check(!swallow && !zoom, "slow second click must not zoom");
        }

        static string TestClickTapSingle() {
            Settings s = DefaultTestSettings();
            s.SmartZoom = true;
            ClickTapDetector d = new ClickTapDetector(s);
            long t = 1000;
            bool zoom;
            d.SetClock(t); d.OnButton(true, out zoom);
            d.SetClock(t += 60); bool swallow = d.OnButton(false, out zoom);
            return Check(!swallow && !zoom, "single click must not zoom");
        }

        static string TestEventSensitivity() {
            Settings s = new Settings();
            s.Enabled = true;
            s.Sensitivity = 10;
            EventGestureEngine e = new EventGestureEngine(s);
            return Check(e.HandleWheel(120) == 240, "sens=10 should double 120 to 240, got " + e.HandleWheel(120))
            ?? Check(new EventGestureEngine(new Settings { Enabled = true, Sensitivity = 1 }).HandleWheel(120) == 24,
                "sens=1 should scale 120 to ~24");
        }

        static string TestEventNatural() {
            Settings s = new Settings();
            s.Enabled = true;
            s.Sensitivity = 5;
            s.NaturalScroll = true;
            EventGestureEngine e = new EventGestureEngine(s);
            return Check(e.HandleWheel(120) == -120, "natural scroll should invert +120 to -120");
        }

        static string TestEventDisabled() {
            Settings s = new Settings();
            s.Enabled = false;
            EventGestureEngine e = new EventGestureEngine(s);
            GestureAction act; List<int> rel; bool sz;
            EventGestureEngine.HwDecision d = e.HandleHWheel(120, out act, out rel, out sz);
            return Check(d == EventGestureEngine.HwDecision.Scroll && rel.Count == 1 && rel[0] == 120,
                "disabled engine should pass HWHEEL straight through")
            ?? Check(e.HandleWheel(120) == 120, "disabled engine should pass WHEEL straight through");
        }

        // ------------------------------------------------------------ pinch zoom

        static string TestPinchZoomIn() {
            Settings s = DefaultTestSettings();
            s.PinchZoom = true;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);

            // two touches land 200 units apart
            Feed2(e, 1, 100, 500, TouchDecoder.STATE_START, 2, 300, 500, TouchDecoder.STATE_START);
            // spread apart: 200 -> 500 units of separation (distance +300)
            for (int i = 0; i < 10; i++) {
                int spread = 200 + i * 30;           // +30 per report
                Feed2(e, 1, 100 - i * 15, 500, TouchDecoder.STATE_DRAG, 2, 100 + spread + i * 15, 500, TouchDecoder.STATE_DRAG);
            }
            return Check(sink.Zooms.Count > 0 && sink.Zooms[0] > 0,
                "spreading fingers should zoom in (positive Ctrl+wheel), got " + sink.Zooms.Count + " zooms");
        }

        static string TestPinchZoomOut() {
            Settings s = DefaultTestSettings();
            s.PinchZoom = true;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);

            Feed2(e, 1, 100, 500, TouchDecoder.STATE_START, 2, 300, 500, TouchDecoder.STATE_START);
            // close together: 200 -> 60 units of separation (distance -140)
            for (int i = 0; i < 8; i++) {
                int spread = 200 - i * 18;
                Feed2(e, 1, 100 + i * 9, 500, TouchDecoder.STATE_DRAG, 2, 100 + spread - i * 9, 500, TouchDecoder.STATE_DRAG);
            }
            return Check(sink.Zooms.Count > 0 && sink.Zooms[0] < 0,
                "closing fingers should zoom out (negative Ctrl+wheel), got " + sink.Zooms.Count + " zooms");
        }

        static string TestPinchDisabled() {
            Settings s = DefaultTestSettings();
            s.PinchZoom = false;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);

            Feed2(e, 1, 100, 500, TouchDecoder.STATE_START, 2, 300, 500, TouchDecoder.STATE_START);
            for (int i = 0; i < 10; i++) {
                int spread = 200 + i * 30;
                Feed2(e, 1, 100 - i * 15, 500, TouchDecoder.STATE_DRAG, 2, 100 + spread + i * 15, 500, TouchDecoder.STATE_DRAG);
            }
            return Check(sink.Zooms.Count == 0, "PinchZoom off should emit no zoom events");
        }

        static string TestPinchSensitivity() {
            // Same spread with sens=10 must emit more zoom notches than sens=1
            int high = CountPinchZooms(10);
            int low = CountPinchZooms(1);
            return Check(high > low,
                "pinch sens 10 (" + high + " zooms) should beat sens 1 (" + low + " zooms) for the same spread");
        }

        static int CountPinchZooms(int pinchSens) {
            Settings s = DefaultTestSettings();
            s.PinchZoom = true;
            s.PinchSensitivity = pinchSens;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);
            Feed2(e, 1, 100, 500, TouchDecoder.STATE_START, 2, 300, 500, TouchDecoder.STATE_START);
            for (int i = 0; i < 10; i++) {
                int spread = 200 + i * 30;
                Feed2(e, 1, 100 - i * 15, 500, TouchDecoder.STATE_DRAG, 2, 100 + spread + i * 15, 500, TouchDecoder.STATE_DRAG);
            }
            return sink.Zooms.Count;
        }

        static string TestTapToClick() {
            Settings s = DefaultTestSettings();
            s.TapToClick = true;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);
            long t = 1000;
            // quick one-finger tap: down then lift, no movement
            e.SetClock(t); Feed(e, 1, 300, 500, TouchDecoder.STATE_START);
            e.SetClock(t + 80); Lift(e);
            return Check(sink.Buttons.Count == 1 && sink.Buttons[0] == 1,
                "quick tap should emit left click, got " + sink.Buttons.Count + " buttons");
        }

        static string TestTapToClickMove() {
            Settings s = DefaultTestSettings();
            s.TapToClick = true;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);
            long t = 1000;
            e.SetClock(t); Feed(e, 1, 300, 500, TouchDecoder.STATE_START);
            // finger moved a lot (a scroll, not a tap)
            e.SetClock(t + 40); Feed(e, 1, 300, 700, TouchDecoder.STATE_DRAG);
            e.SetClock(t + 80); Feed(e, 1, 300, 900, TouchDecoder.STATE_DRAG);
            e.SetClock(t + 120); Lift(e);
            return Check(sink.Buttons.Count == 0, "moving finger must not be a tap");
        }

        static string TestTwoFingerMiddle() {
            Settings s = DefaultTestSettings();
            s.TwoFingerTapMiddle = true;
            s.SmartZoom = false;   // isolate the middle-click path
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);
            long t = 1000;
            // two fingers down, then both lift, no movement
            e.SetClock(t); Feed2(e, 1, 100, 500, TouchDecoder.STATE_START, 2, 300, 500, TouchDecoder.STATE_START);
            e.SetClock(t + 80); Lift(e);
            return Check(sink.Buttons.Count == 1 && sink.Buttons[0] == 4,
                "two-finger tap should emit middle click (4), got " + (sink.Buttons.Count > 0 ? sink.Buttons[0].ToString() : "none"));
        }

        static string TestNewActions() {
            // new bindable actions must exist and have names
            return Check(GestureActionNames.Name(GestureAction.DesktopLeft).ToLowerInvariant().Contains("virtual desktop"),
                "DesktopLeft missing name: '" + GestureActionNames.Name(GestureAction.DesktopLeft) + "'")
            ?? Check(GestureActionNames.Name(GestureAction.BrowserBack).Length > 0, "BrowserBack missing name")
            ?? Check(ArrayContains(GestureActionNames.All, GestureAction.DesktopRight), "DesktopRight not in All list");
        }

        static bool ArrayContains(GestureAction[] arr, GestureAction a) {
            foreach (GestureAction x in arr) if (x == a) return true;
            return false;
        }

        /// <summary>Lift all fingers (feed an empty report).</summary>
        static void Lift(GestureEngine e) {
            e.Process(new TouchReport());
        }

        /// <summary>A clean two-finger tap at the given clock time: both down, then up.</summary>
        static void TwoFingerTap(GestureEngine e, long tDown, long tUp) {
            e.SetClock(tDown);
            Feed2(e, 1, 100, 500, TouchDecoder.STATE_START, 2, 300, 500, TouchDecoder.STATE_START);
            e.SetClock(tUp);
            Lift(e);
        }

        static string TestSmartZoomDoubleTap() {
            Settings s = DefaultTestSettings();
            s.SmartZoom = true;
            s.PinchZoom = false;   // isolate smart zoom
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);
            long t = 1000;
            TwoFingerTap(e, t, t + 80);          // tap 1
            TwoFingerTap(e, t + 200, t + 280);   // tap 2 within the window
            return Check(sink.Zooms.Count == 1 && sink.Zooms[0] > 0,
                "double-tap should emit one zoom-in, got " + sink.Zooms.Count + " zooms");
        }

        static string TestSmartZoomSingleTap() {
            Settings s = DefaultTestSettings();
            s.SmartZoom = true;
            s.PinchZoom = false;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);
            TwoFingerTap(e, 1000, 1080);         // only one tap
            e.SetClock(2000);
            Lift(e);
            return Check(sink.Zooms.Count == 0, "single tap should not zoom");
        }

        static string TestSmartZoomSlowTaps() {
            Settings s = DefaultTestSettings();
            s.SmartZoom = true;
            s.PinchZoom = false;
            TestSink sink = new TestSink();
            GestureEngine e = NewEngine(sink, s);
            long t = 1000;
            TwoFingerTap(e, t, t + 80);           // tap 1
            TwoFingerTap(e, t + 900, t + 980);    // tap 2 too late (> 320 ms gap)
            return Check(sink.Zooms.Count == 0, "slow double-tap should not zoom");
        }
    }
}
