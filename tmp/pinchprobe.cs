using System;
using System.IO;
using System.Runtime.InteropServices;

class PinchProbe {
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
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")]
    static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("kernel32.dll")]
    static extern void Sleep(uint ms);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int pt_x; public int pt_y; }

    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEMOVE = 0x200;
    const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    const int WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
    const int WM_MOUSEWHEEL = 0x20A;
    const int WM_MOUSEHWHEEL = 0x20E;
    const uint PM_REMOVE = 1;

    static string logPath = "C:\\Users\\dir.minister\\.asterxa-ai\\desktop-workspace\\MagicMouseEnhancer\\tmp\\pinch-events.txt";
    static LowLevelMouseProc _proc;
    static IntPtr _hook;
    static uint _lastMoveLog;

    static string MsgName(int m) {
        switch (m) {
            case WM_MOUSEMOVE: return "MOVE";
            case WM_LBUTTONDOWN: return "L_DOWN";
            case WM_LBUTTONUP: return "L_UP";
            case WM_RBUTTONDOWN: return "R_DOWN";
            case WM_RBUTTONUP: return "R_UP";
            case WM_MOUSEWHEEL: return "WHEEL";
            case WM_MOUSEHWHEEL: return "HWHEEL";
            default: return "0x" + m.ToString("X");
        }
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            int msg = wParam.ToInt32();
            MSLLHOOKSTRUCT hs = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            int delta = (short)((hs.mouseData >> 16) & 0xFFFF);
            uint t = unchecked((uint)Environment.TickCount);
            bool interesting = msg != WM_MOUSEMOVE || t - _lastMoveLog >= 40;
            if (msg == WM_MOUSEMOVE && t - _lastMoveLog < 40) interesting = false;
            if (msg == WM_MOUSEMOVE) _lastMoveLog = t;
            if (interesting) {
                string line = t.ToString("D8") + " " + MsgName(msg).PadRight(7)
                    + " d=" + delta.ToString("+0;-0").PadLeft(4)
                    + " x=" + hs.pt_x + " y=" + hs.pt_y
                    + " info=0x" + ((long)hs.dwExtraInfo).ToString("X8");
                File.AppendAllText(logPath, line + "\r\n");
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static int Main() {
        File.WriteAllText(logPath, "=== Pinch probe — " + DateTime.Now.ToString("HH:mm:ss") + " ===\r\n");
        Console.WriteLine("Hook installed. 60 seconds. Do these on the Magic Mouse:");
        Console.WriteLine("  1) PINCH OUT (two fingers spread apart)  x3");
        Console.WriteLine("  2) PINCH IN  (two fingers come together) x3");
        Console.WriteLine("  3) two-finger DOUBLE-TAP x3");
        Console.WriteLine("  4) normal scroll (for comparison)");
        _proc = new LowLevelMouseProc(HookProc);
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) { Console.WriteLine("hook failed: " + Marshal.GetLastWin32Error()); return 1; }

        DateTime end = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < end) {
            MSG msg;
            while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE)) {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            Sleep(10);
        }
        UnhookWindowsHookEx(_hook);
        Console.WriteLine("Done.");
        return 0;
    }
}
