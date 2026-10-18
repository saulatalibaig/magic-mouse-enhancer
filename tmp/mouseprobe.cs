using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

class MouseProbe {
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT {
        public int pt_x, pt_y;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int pt_x; public int pt_y; }

    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEMOVE = 0x200;
    const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    const int WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
    const int WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208;
    const int WM_MOUSEWHEEL = 0x20A;
    const int WM_XBUTTONDOWN = 0x20B, WM_XBUTTONUP = 0x20C;
    const int WM_MOUSEHWHEEL = 0x20E;

    static string logPath = "C:\\Users\\dir.minister\\.asterxa-ai\\desktop-workspace\\MagicMouseEnhancer\\tmp\\mouse-events.txt";
    static LowLevelMouseProc _proc;
    static IntPtr _hook;

    static string MsgName(int m) {
        switch (m) {
            case WM_MOUSEMOVE: return "MOVE";
            case WM_LBUTTONDOWN: return "L_DOWN";
            case WM_LBUTTONUP: return "L_UP";
            case WM_RBUTTONDOWN: return "R_DOWN";
            case WM_RBUTTONUP: return "R_UP";
            case WM_MBUTTONDOWN: return "M_DOWN";
            case WM_MBUTTONUP: return "M_UP";
            case WM_MOUSEWHEEL: return "WHEEL";
            case WM_MOUSEHWHEEL: return "HWHEEL";
            case WM_XBUTTONDOWN: return "X_DOWN";
            case WM_XBUTTONUP: return "X_UP";
            default: return "0x" + m.ToString("X");
        }
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            int msg = wParam.ToInt32();
            MSLLHOOKSTRUCT hs = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            int delta = (short)((hs.mouseData >> 16) & 0xFFFF);
            uint t = unchecked((uint)Environment.TickCount);
            // Log only interesting events (skip pure MOVE spam)
            bool interesting = msg != WM_MOUSEMOVE || hs.flags != 0;
            if (msg == WM_MOUSEMOVE) {
                // log move only every ~30ms to keep file small
                if (t - _lastMoveLog < 30) interesting = false;
                else _lastMoveLog = t;
            }
            if (interesting) {
                string line = t.ToString("D8") + " " + MsgName(msg).PadRight(7)
                    + " d=" + delta.ToString("+0;-0").PadLeft(4)
                    + " x=" + hs.pt_x + " y=" + hs.pt_y;
                lock (_sync) { File.AppendAllText(logPath, line + "\r\n"); }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static uint _lastMoveLog;
    static readonly object _sync = new object();

    static int Main() {
        File.WriteAllText(logPath, "=== Mouse event probe — " + DateTime.Now.ToString("HH:mm:ss") + " ===\r\n");
        Console.WriteLine("Hook installed. For 30 seconds, perform these gestures on the Magic Mouse:");
        Console.WriteLine("  1) one-finger scroll UP and DOWN (vertical)");
        Console.WriteLine("  2) one-finger scroll LEFT and RIGHT (horizontal)");
        Console.WriteLine("  3) two-finger swipe LEFT and RIGHT");
        Console.WriteLine("  4) two-finger swipe UP and DOWN");
        Console.WriteLine("  5) two-finger TAP (click)");
        Console.WriteLine("  6) single click, then double-click");
        Console.WriteLine("  -> GO NOW...");
        _proc = new LowLevelMouseProc(HookProc);
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) { Console.WriteLine("hook failed: " + Marshal.GetLastWin32Error()); return 1; }

        DateTime end = DateTime.UtcNow.AddSeconds(30);
        MSG msg;
        while (DateTime.UtcNow < end && GetMessage(out msg, IntPtr.Zero, 0, 0)) {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(_hook);
        Console.WriteLine("Done. Log written to " + logPath);
        return 0;
    }
}
