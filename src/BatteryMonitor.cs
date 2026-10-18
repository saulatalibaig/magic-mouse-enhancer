using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MagicMouseEnhancer.Hid;

namespace MagicMouseEnhancer {
    /// <summary>
    /// Reads the Magic Mouse battery level.
    ///   1) USB / raw-HID mode: HID feature report 0x02 returns the battery
    ///      percentage (byte 1). Sent through the vendor-defined collection.
    ///   2) Bluetooth / driver mode: the DEVPKEY_Device_BatteryLevel property
    ///      on the mouse's HID node (Windows reads the BT battery service).
    /// Returns -1 when the level cannot be read (driver owns the device, etc).
    /// </summary>
    public static class BatteryMonitor {
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr infoSet, uint index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInstanceId(IntPtr infoSet, ref SP_DEVINFO_DATA data, IntPtr buffer, uint size, out uint needed);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceProperty(IntPtr infoSet, ref SP_DEVINFO_DATA data, ref Guid propertyKey, uint propertyType, IntPtr buffer, uint bufferSize, out uint required, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr infoSet);

        static readonly Guid DEVPKEY_Device_BatteryLevel_Key = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
        const uint DEVPKEY_Device_BatteryLevel_Pid = 2;   // property id within the key        const uint DIGCF_PRESENT = 0x2;
        const uint DEVPROP_TYPE_UINT32 = 7;

        static readonly Guid HidGuid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

        public static int ReadBatteryPct(ushort vid, ushort pid) {
            // Try the SetupAPI property first (works for BT devices that expose a
            // battery service; the Apple driver may surface it here).
            int pct = ReadPnpBatteryProperty();
            if (pct >= 0) return pct;

            // Fall back to the HID feature report 0x02 (USB raw-HID mode).
            return ReadHidBatteryReport(vid, pid);
        }

        static int ReadPnpBatteryProperty() {
            try {
                Guid guid = HidGuid;
                IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, HidApi.DIGCF_PRESENT);
                if (set == IntPtr.Zero || set == (IntPtr)(-1)) return -1;
                try {
                    uint idx = 0;
                    while (true) {
                        SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
                        d.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                        if (!SetupDiEnumDeviceInfo(set, idx, ref d)) break;
                        idx++;

                        // get instance id to match Apple vendor
                        uint needed = 0;
                        SetupDiGetDeviceInstanceId(set, ref d, IntPtr.Zero, 0, out needed);
                        if (needed == 0 || needed > 512) continue;
                        IntPtr buf = Marshal.AllocHGlobal((int)needed);
                        try {
                            if (!SetupDiGetDeviceInstanceId(set, ref d, buf, needed, out needed)) continue;
                            string id = Marshal.PtrToStringUni(buf);
                            if (id == null) continue;
                            string low = id.ToLowerInvariant();
                            bool apple = low.IndexOf("vid_004c", StringComparison.Ordinal) >= 0
                                      || low.IndexOf("004c", StringComparison.Ordinal) >= 0
                                      || low.IndexOf("05ac", StringComparison.Ordinal) >= 0;
                            if (!apple) continue;

                            uint required = 0;
                            IntPtr val = Marshal.AllocHGlobal(8);
                            try {
                                Guid key = DEVPKEY_Device_BatteryLevel_Key;
                                if (SetupDiGetDeviceProperty(set, ref d, ref key,
                                        DEVPROP_TYPE_UINT32, val, 8, out required, 0)) {
                                    int level = Marshal.ReadInt32(val);
                                    if (level >= 0 && level <= 100) return level;
                                }
                            } finally { Marshal.FreeHGlobal(val); }
                        } finally { Marshal.FreeHGlobal(buf); }
                    }
                } finally { SetupDiDestroyDeviceInfoList(set); }
            } catch {
            }
            return -1;
        }

        static int ReadHidBatteryReport(ushort vid, ushort pid) {
            try {
                List<string> paths = HidApi.EnumerateHidDevicePaths();
                foreach (string path in paths) {
                    try {
                        ushort v, p;
                        if (!ReadAttributes(path, out v, out p)) continue;
                        if (v != vid || p != pid) continue;
                        IntPtr h = HidApi.CreateFile(path, HidApi.GENERIC_READ | HidApi.GENERIC_WRITE,
                            HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (HidApi.IsInvalidHandle(h)) continue;
                        try {
                            byte[] rep = new byte[4];
                            rep[0] = 0x02;   // battery feature report
                            if (HidApi.HidD_GetFeature(h, rep, rep.Length)) {
                                // byte 1 = percentage (0-100) for MM2; some firmwares use byte 2
                                int b1 = rep[1];
                                int b2 = rep[2];
                                if (b1 >= 0 && b1 <= 100) return b1;
                                if (b2 >= 0 && b2 <= 100) return b2;
                            }
                        } finally { HidApi.CloseHandle(h); }
                    } catch { }
                }
            } catch {
            }
            return -1;
        }

        static bool ReadAttributes(string path, out ushort vid, out ushort pid) {
            vid = 0; pid = 0;
            IntPtr h = HidApi.CreateFile(path, HidApi.GENERIC_READ,
                HidApi.FILE_SHARE_READ | HidApi.FILE_SHARE_WRITE, IntPtr.Zero, HidApi.OPEN_EXISTING, 0, IntPtr.Zero);
            if (HidApi.IsInvalidHandle(h)) return false;
            try {
                HidApi.HIDD_ATTRIBUTES attrs = new HidApi.HIDD_ATTRIBUTES();
                attrs.Size = 4 + 2 + 2 + 2;
                if (!HidApi.HidD_GetAttributes(h, ref attrs)) return false;
                vid = attrs.VendorID;
                pid = attrs.ProductID;
                return true;
            } finally {
                HidApi.CloseHandle(h);
            }
        }
    }
}
