using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MagicMouseEnhancer.Hid;

class Probe3 {
    static int Main() {
        Console.WriteLine("=== Probe3: output-report hail-mary + full access matrix ===");
        List<string> paths = HidApi.EnumerateHidDevicePaths();
        foreach (string p in paths) {
            if (p.IndexOf("pid&0269", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine("\nPATH: " + p);

            IntPtr h = HidApi.CreateFile(p, HidApi.GENERIC_READ | HidApi.GENERIC_WRITE,
                HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            if (HidApi.IsInvalidHandle(h)) { Console.WriteLine("   open RW failed win32=" + Marshal.GetLastWin32Error()); continue; }
            Console.WriteLine("   opened RW");

            bool sf = HidApi.HidD_SetFeature(h, new byte[] { 0xF1, 0x02, 0x01 }, 3);
            Console.WriteLine("   SetFeature F1 02 01: " + (sf ? "OK" : "FAIL win32=" + Marshal.GetLastWin32Error()));

            bool so = HidApi.HidD_SetOutputReport(h, new byte[] { 0xF1, 0x02, 0x01 }, 3);
            Console.WriteLine("   SetOutputReport F1 02 01: " + (so ? "OK" : "FAIL win32=" + Marshal.GetLastWin32Error()));

            byte[] buf = new byte[64];
            buf[0] = 0xF1;
            bool gf = HidApi.HidD_GetFeature(h, buf, 64);
            Console.WriteLine("   GetFeature 0xF1: " + (gf ? "OK" : "FAIL win32=" + Marshal.GetLastWin32Error()));

            // check preparsed data to see what the descriptor actually declares
            IntPtr ppd;
            if (HidApi.HidD_GetPreparsedData(h, out ppd)) {
                int capsSize = Marshal.SizeOf(typeof(HidApi.HIDP_CAPS));
                IntPtr capsPtr = Marshal.AllocHGlobal(capsSize);
                if (HidApi.HidP_GetCaps(ppd, capsPtr) == 0) {
                    HidApi.HIDP_CAPS caps = (HidApi.HIDP_CAPS)Marshal.PtrToStructure(capsPtr, typeof(HidApi.HIDP_CAPS));
                    Console.WriteLine("   Caps: usage=" + caps.UsagePage.ToString("X2") + "/" + caps.Usage.ToString("X2")
                        + "  NumInputButtonCaps=" + caps.NumberInputButtonCaps
                        + "  NumInputValueCaps=" + caps.NumberInputValueCaps
                        + "  NumOutputValueCaps=" + caps.NumberOutputValueCaps
                        + "  NumFeatureButtonCaps=" + caps.NumberFeatureButtonCaps
                        + "  NumFeatureValueCaps=" + caps.NumberFeatureValueCaps
                        + "  NumInputDataIndices=" + caps.NumberInputDataIndices);
                }
                Marshal.FreeHGlobal(capsPtr);
                HidApi.HidD_FreePreparsedData(ppd);
            } else {
                Console.WriteLine("   GetPreparsedData FAIL win32=" + Marshal.GetLastWin32Error());
            }
            HidApi.CloseHandle(h);
        }
        Console.WriteLine("=== done ===");
        return 0;
    }
}
