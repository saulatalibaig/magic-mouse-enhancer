using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MagicMouseEnhancer.Hid;

class Probe {
    static IntPtr OpenPath(string path) {
        // Try RW first (needed for SetFeature), then R
        IntPtr h = HidApi.CreateFile(path, HidApi.GENERIC_READ | HidApi.GENERIC_WRITE,
            HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
        if (HidApi.IsInvalidHandle(h)) {
            Console.WriteLine("   RW open failed win32=" + Marshal.GetLastWin32Error() + ", trying R");
            h = HidApi.CreateFile(path, HidApi.GENERIC_READ,
                HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            if (HidApi.IsInvalidHandle(h)) { Console.WriteLine("   R open failed win32=" + Marshal.GetLastWin32Error()); return IntPtr.Zero; }
            Console.WriteLine("   opened with GENERIC_READ only");
        } else {
            Console.WriteLine("   opened with GENERIC_READ|GENERIC_WRITE");
        }
        return h;
    }

    static void TrySetFeature(IntPtr h, byte[] report, string label) {
        bool ok = HidApi.HidD_SetFeature(h, report, report.Length);
        Console.WriteLine("   SetFeature " + label + " -> " + (ok ? "OK" : "FAIL win32=" + Marshal.GetLastWin32Error()));
    }

    static void GetFeatureIds(IntPtr h) {
        // Try to read feature reports for a few candidate IDs to see what the device supports
        byte[] buf = new byte[64];
        int[] ids = { 0xF1, 0xD7, 0x02, 0x03, 0x01, 0x00 };
        foreach (int id in ids) {
            for (int i = 0; i < buf.Length; i++) buf[i] = 0;
            buf[0] = (byte)id;
            if (HidApi.HidD_GetFeature(h, buf, buf.Length)) {
                Console.Write("   GetFeature id=0x" + id.ToString("X2") + " OK:");
                int n = Math.Min(buf.Length, 16);
                for (int i = 0; i < n; i++) Console.Write(" " + buf[i].ToString("X2"));
                Console.WriteLine();
            } else {
                Console.WriteLine("   GetFeature id=0x" + id.ToString("X2") + " FAIL win32=" + Marshal.GetLastWin32Error());
            }
        }
    }

    static void ReadReports(IntPtr h, int seconds) {
        byte[] buffer = new byte[512];
        Console.WriteLine("   reading reports for " + seconds + "s ...");
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        int count = 0;
        while (DateTime.UtcNow < end) {
            uint read = 0;
            if (HidApi.ReadFile(h, buffer, (uint)buffer.Length, out read, IntPtr.Zero)) {
                if (read > 0) {
                    count++;
                    if (count <= 12) {
                        Console.Write("   report (" + read + " bytes):");
                        int n = Math.Min((int)read, 32);
                        for (int i = 0; i < n; i++) Console.Write(" " + buffer[i].ToString("X2"));
                        Console.WriteLine();
                    }
                }
            } else {
                Console.WriteLine("   read ended win32=" + Marshal.GetLastWin32Error());
                break;
            }
        }
        Console.WriteLine("   total reports: " + count);
    }

    static int Main() {
        Console.WriteLine("=== Magic Mouse 2 deep probe ===");
        List<string> paths = HidApi.EnumerateHidDevicePaths();
        string target = null;
        foreach (string p in paths) {
            if (p.IndexOf("pid&0269", StringComparison.OrdinalIgnoreCase) >= 0 &&
                p.IndexOf("col02", StringComparison.OrdinalIgnoreCase) >= 0) {
                target = p; break;
            }
        }
        if (target == null) { Console.WriteLine("col02 interface of PID 0269 not found!"); return 1; }
        Console.WriteLine("Target: " + target);

        IntPtr h = OpenPath(target);
        if (h == IntPtr.Zero) return 1;

        GetFeatureIds(h);

        Console.WriteLine("--- attempt sequence (kernel-style: expect EIO then retry) ---");
        for (int attempt = 1; attempt <= 5; attempt++) {
            TrySetFeature(h, new byte[] { 0xF1, 0x02, 0x01 }, "F1 02 01 (attempt " + attempt + ")");
            Thread.Sleep(500);
        }
        TrySetFeature(h, new byte[] { 0xD7, 0x01 }, "D7 01 (MM1-style)");

        // Now see if touch reports flow
        ReadReports(h, 8);

        HidApi.CloseHandle(h);
        Console.WriteLine("=== done ===");
        return 0;
    }
}
