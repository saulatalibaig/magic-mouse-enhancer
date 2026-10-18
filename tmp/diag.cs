using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MagicMouseEnhancer.Hid;

class Diag {
    static int Main() {
        Console.WriteLine("=== HID diagnostic ===");
        Console.WriteLine("Now: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        List<string> paths = HidApi.EnumerateHidDevicePaths();
        Console.WriteLine("HID interface paths found: " + paths.Count);
        foreach (string p in paths) {
            Console.WriteLine("\nPATH: " + p);
            IntPtr h = HidApi.CreateFile(p, HidApi.GENERIC_READ,
                HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE,
                IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            if (HidApi.IsInvalidHandle(h)) {
                int errR = Marshal.GetLastWin32Error();
                h = HidApi.CreateFile(p, HidApi.GENERIC_READ | HidApi.GENERIC_WRITE,
                    HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE,
                    IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
                if (HidApi.IsInvalidHandle(h)) {
                    Console.WriteLine("   open READ failed (win32 " + errR + "), open RW failed (win32 " + Marshal.GetLastWin32Error() + ")");
                    continue;
                }
                Console.WriteLine("   open READ failed (win32 " + errR + "), open RW OK");
            } else {
                Console.WriteLine("   open READ OK");
            }
            HidApi.HIDD_ATTRIBUTES a = new HidApi.HIDD_ATTRIBUTES();
            a.Size = 10;
            bool ok = HidApi.HidD_GetAttributes(h, ref a);
            if (ok) {
                Console.WriteLine("   VID=0x" + a.VendorID.ToString("X4") + "  PID=0x" + a.ProductID.ToString("X4") + "  version=0x" + a.VersionNumber.ToString("X4"));
                Console.WriteLine("   IsMagicMouse=" + MagicMouse.IsMagicMouse(a.VendorID, a.ProductID));
            } else {
                Console.WriteLine("   HidD_GetAttributes failed (win32 " + Marshal.GetLastWin32Error() + ")");
            }
            HidApi.CloseHandle(h);
        }
        return 0;
    }
}
