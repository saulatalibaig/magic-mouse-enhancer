using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MagicMouseEnhancer.Hid;

class Capture {
    static int Main() {
        Console.WriteLine("=== Capture: raw reports from ALL Magic Mouse interfaces for 20s ===");
        Console.WriteLine(">>> NOW PLEASE SWIPE / SCROLL / TOUCH THE MAGIC MOUSE SURFACE repeatedly <<<");
        Console.WriteLine(">>> (one finger scrolling, two-finger swipes, click the surface)         <<<");
        Console.WriteLine();

        List<string> paths = HidApi.EnumerateHidDevicePaths();
        var targets = new List<string>();
        foreach (string p in paths) {
            if (p.IndexOf("pid&0269", StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.IndexOf("vid&0001004c", StringComparison.OrdinalIgnoreCase) >= 0)
                targets.Add(p);
        }
        if (targets.Count == 0) { Console.WriteLine("No Magic Mouse interfaces found!"); return 1; }

        DateTime end = DateTime.UtcNow.AddSeconds(20);
        int[] counts = new int[targets.Count];
        var handles = new IntPtr[targets.Count];
        var buffers = new byte[targets.Count][];
        var stop = false;

        for (int i = 0; i < targets.Count; i++) {
            handles[i] = HidApi.CreateFile(targets[i], HidApi.GENERIC_READ,
                HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            buffers[i] = new byte[512];
            Console.WriteLine("[" + i + "] " + (HidApi.IsInvalidHandle(handles[i]) ? "OPEN FAILED win32=" + Marshal.GetLastWin32Error() : "OPENED") + "  " + targets[i]);
            if (HidApi.IsInvalidHandle(handles[i])) handles[i] = IntPtr.Zero;
        }
        Console.WriteLine("--- capturing (20s)... ---");

        var readers = new List<Thread>();
        var reportLog = new System.Collections.Generic.List<string>();
        for (int i = 0; i < targets.Count; i++) {
            int idx = i;
            Thread t = new Thread(() => {
                if (handles[idx] == IntPtr.Zero) return;
                while (!stop && DateTime.UtcNow < end) {
                    uint read = 0;
                    if (HidApi.ReadFile(handles[idx], buffers[idx], (uint)buffers[idx].Length, out read, IntPtr.Zero)) {
                        if (read > 0) {
                            counts[idx]++;
                            if (counts[idx] <= 40) {
                                string s = "[" + idx + "] " + read + "B:";
                                int n = Math.Min((int)read, 24);
                                for (int b = 0; b < n; b++) s += " " + buffers[idx][b].ToString("X2");
                                lock (reportLog) reportLog.Add(s);
                            }
                        }
                    } else {
                        lock (reportLog) reportLog.Add("[" + idx + "] read ended win32=" + Marshal.GetLastWin32Error());
                        break;
                    }
                }
            });
            t.IsBackground = true;
            t.Start();
            readers.Add(t);
        }

        while (DateTime.UtcNow < end) Thread.Sleep(200);
        stop = true;
        Thread.Sleep(300);

        Console.WriteLine("--- results ---");
        foreach (string s in reportLog) Console.WriteLine(s);
        for (int i = 0; i < targets.Count; i++)
            Console.WriteLine("[" + i + "] total reports: " + counts[i]);
        for (int i = 0; i < targets.Count; i++)
            if (handles[i] != IntPtr.Zero) HidApi.CloseHandle(handles[i]);
        Console.WriteLine("=== done ===");
        return 0;
    }
}
