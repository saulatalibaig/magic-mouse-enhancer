using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

class Diag2 {
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("hid.dll", SetLastError = true)]
    static extern void HidD_GetHidGuid(ref Guid guid);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid classGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ = 1;
    const uint FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3;
    const uint DIGCF_PRESENT = 0x2;
    const uint DIGCF_DEVICEINTERFACE = 0x10;

    static void DumpHex(byte[] b, int n) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n; i++) {
            sb.Append(b[i].ToString("X2"));
            sb.Append(' ');
            if ((i + 1) % 16 == 0) sb.AppendLine();
        }
        Console.WriteLine(sb.ToString());
    }

    static int Main() {
        Console.WriteLine("=== Diag2: fixed CharSet.Unicode enumeration + raw dump ===");
        Guid hidGuid = Guid.Empty;
        HidD_GetHidGuid(ref hidGuid);
        IntPtr devInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devInfoSet == IntPtr.Zero || devInfoSet == (IntPtr)(-1)) { Console.WriteLine("GetClassDevs failed"); return 1; }
        int count = 0;
        uint index = 0;
        while (true) {
            SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            if (!SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, index, ref ifData)) break;
            index++;
            uint required = 0;
            SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifData, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required == 0 || required > 8192) continue;
            IntPtr detail = Marshal.AllocHGlobal((int)required);
            try {
                Marshal.WriteInt32(detail, 8); // cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA) on x64
                if (SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifData, detail, required, out required, IntPtr.Zero)) {
                    count++;
                    Console.WriteLine("\n--- path #" + count + " (buffer size " + required + ") ---");
                    byte[] raw = new byte[64];
                    for (int i = 0; i < 64; i++) raw[i] = Marshal.ReadByte(detail, i);
                    DumpHex(raw, 64);

                    string uni8 = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 8));
                    string uni4 = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                    string ansi8 = Marshal.PtrToStringAnsi(new IntPtr(detail.ToInt64() + 8));
                    string ansi4 = Marshal.PtrToStringAnsi(new IntPtr(detail.ToInt64() + 4));
                    Console.WriteLine("Uni@8 : " + uni8);
                    Console.WriteLine("Uni@4 : " + uni4);
                    Console.WriteLine("Ansi@8: " + ansi8);
                    Console.WriteLine("Ansi@4: " + ansi4);

                    string path = uni8;
                    if (!string.IsNullOrEmpty(path) && path.StartsWith("\\\\?\\")) {
                        IntPtr h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (h != IntPtr.Zero && h != (IntPtr)(-1)) {
                            HIDD_ATTRIBUTES a = new HIDD_ATTRIBUTES();
                            a.Size = 10;
                            if (HidD_GetAttributes(h, ref a)) {
                                Console.WriteLine("   OPEN OK  VID=0x" + a.VendorID.ToString("X4") + " PID=0x" + a.ProductID.ToString("X4") + "  IsMagicMouse=" +
                                    (a.VendorID == 0x05AC || a.VendorID == 0x004C) + " pidMatch=" + (a.ProductID == 0x030D || a.ProductID == 0x0269 || a.ProductID == 0x029F));
                            }
                            CloseHandle(h);
                        } else {
                            Console.WriteLine("   open failed win32=" + Marshal.GetLastWin32Error());
                        }
                    }
                }
            } finally { Marshal.FreeHGlobal(detail); }
        }
        SetupDiDestroyDeviceInfoList(devInfoSet);
        Console.WriteLine("\nTotal paths with Unicode enumeration: " + count);
        return 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
}
