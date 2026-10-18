using System;
using System.Runtime.InteropServices;

namespace MagicMouseEnhancer.Core {
    /// <summary>
    /// Injects synthetic input through SendInput: mouse wheel / horizontal wheel
    /// events and keyboard shortcuts for gestures.
    /// </summary>
    public static class NativeInput {
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUT {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public MOUSEINPUT mi;
            [FieldOffset(8)] public KEYBDINPUT ki;
            [FieldOffset(8)] public HARDWAREINPUT hi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        const uint INPUT_MOUSE = 0;
        const uint INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_WHEEL = 0x0800;
        const uint MOUSEEVENTF_HWHEEL = 0x01000;
        const uint KEYEVENTF_KEYUP = 0x0002;

        const ushort VK_LMENU = 0xA4;
        const ushort VK_LCONTROL = 0xA2;
        const ushort VK_LWIN = 0x5B;
        const ushort VK_TAB = 0x09;
        const ushort VK_LEFT = 0x25;
        const ushort VK_RIGHT = 0x27;
        const ushort VK_PRIOR = 0x21;
        const ushort VK_NEXT = 0x22;
        const ushort VK_HOME = 0x24;
        const ushort VK_END = 0x23;
        const ushort VK_D = 0x44;

        public static void SendWheel(int deltaY, int deltaX) {
            if (deltaY != 0) {
                INPUT i = new INPUT();
                i.type = INPUT_MOUSE;
                i.mi.dwFlags = MOUSEEVENTF_WHEEL;
                i.mi.mouseData = unchecked((uint)deltaY);
                i.mi.dwExtraInfo = MouseHook.MagicMarker;
                SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
            }
            if (deltaX != 0) {
                INPUT i = new INPUT();
                i.type = INPUT_MOUSE;
                i.mi.dwFlags = MOUSEEVENTF_HWHEEL;
                i.mi.mouseData = unchecked((uint)deltaX);
                i.mi.dwExtraInfo = MouseHook.MagicMarker;
                SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
            }
        }

        public static void SendGestureAction(GestureAction action) {
            switch (action) {
                case GestureAction.Back: KeyCombo(new ushort[] { VK_LMENU, VK_LEFT }); break;
                case GestureAction.Forward: KeyCombo(new ushort[] { VK_LMENU, VK_RIGHT }); break;
                case GestureAction.TaskView: KeyCombo(new ushort[] { VK_LWIN, VK_TAB }); break;
                case GestureAction.ShowDesktop: KeyCombo(new ushort[] { VK_LWIN, VK_D }); break;
                case GestureAction.PageUp: KeyCombo(new ushort[] { VK_PRIOR }); break;
                case GestureAction.PageDown: KeyCombo(new ushort[] { VK_NEXT }); break;
                case GestureAction.Home: KeyCombo(new ushort[] { VK_HOME }); break;
                case GestureAction.End: KeyCombo(new ushort[] { VK_END }); break;
                case GestureAction.DesktopLeft: KeyCombo(new ushort[] { VK_LWIN, VK_LCONTROL, VK_LEFT }); break;
                case GestureAction.DesktopRight: KeyCombo(new ushort[] { VK_LWIN, VK_LCONTROL, VK_RIGHT }); break;
                case GestureAction.BrowserBack: SendMouseButton(MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1); break;
                case GestureAction.BrowserForward: SendMouseButton(MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2); break;
                default: break; // None
            }
        }

        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint MOUSEEVENTF_XDOWN = 0x0080;
        const uint MOUSEEVENTF_XUP = 0x0100;
        const uint XBUTTON1 = 0x0001;
        const uint XBUTTON2 = 0x0002;

        /// <summary>Click a mouse button: 1=left, 2=right, 4=middle (tap-to-click,
        /// two-finger tap). Down+up pair via SendInput.</summary>
        public static void SendMouseButton(int button) {
            uint down = 0, up = 0, data = 0;
            switch (button) {
                case 1: down = MOUSEEVENTF_LEFTDOWN; up = MOUSEEVENTF_LEFTUP; break;
                case 2: down = MOUSEEVENTF_RIGHTDOWN; up = MOUSEEVENTF_RIGHTUP; break;
                case 4: down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; break;
                default: return;
            }
            INPUT[] seq = new INPUT[2];
            seq[0].type = INPUT_MOUSE;
            seq[0].mi.dwFlags = down;
            seq[0].mi.dwExtraInfo = MouseHook.MagicMarker;
            seq[1].type = INPUT_MOUSE;
            seq[1].mi.dwFlags = up;
            seq[1].mi.dwExtraInfo = MouseHook.MagicMarker;
            SendInput(2, seq, Marshal.SizeOf(typeof(INPUT)));
        }

        static void SendMouseButton(uint down, uint up, uint data) {
            INPUT[] seq = new INPUT[2];
            seq[0].type = INPUT_MOUSE;
            seq[0].mi.dwFlags = down;
            seq[0].mi.mouseData = data;
            seq[0].mi.dwExtraInfo = MouseHook.MagicMarker;
            seq[1].type = INPUT_MOUSE;
            seq[1].mi.dwFlags = up;
            seq[1].mi.mouseData = data;
            seq[1].mi.dwExtraInfo = MouseHook.MagicMarker;
            SendInput(2, seq, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>Pinch-to-zoom: hold Ctrl, emit one wheel step, release Ctrl.
        /// Ctrl+wheel is the universal zoom shortcut (browsers, Office, PDF, maps).</summary>
        public static void SendZoom(int wheelDelta) {
            if (wheelDelta == 0) return;
            INPUT[] seq = new INPUT[3];
            seq[0].type = INPUT_KEYBOARD;
            seq[0].ki.wVk = VK_LCONTROL;
            seq[1].type = INPUT_MOUSE;
            seq[1].mi.dwFlags = MOUSEEVENTF_WHEEL;
            seq[1].mi.mouseData = unchecked((uint)wheelDelta);
            seq[1].mi.dwExtraInfo = MouseHook.MagicMarker;
            seq[2].type = INPUT_KEYBOARD;
            seq[2].ki.wVk = VK_LCONTROL;
            seq[2].ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(3, seq, Marshal.SizeOf(typeof(INPUT)));
        }

        static void KeyCombo(ushort[] keys) {
            int n = keys.Length;
            INPUT[] list = new INPUT[n * 2];
            for (int i = 0; i < n; i++) {
                list[i].type = INPUT_KEYBOARD;
                list[i].ki.wVk = keys[i];
                list[i + n].type = INPUT_KEYBOARD;
                list[i + n].ki.wVk = keys[n - 1 - i]; // release in reverse order
                list[i + n].ki.dwFlags = KEYEVENTF_KEYUP;
            }
            SendInput((uint)list.Length, list, Marshal.SizeOf(typeof(INPUT)));
        }
    }

    /// <summary>Real sink: forwards engine output to the OS via SendInput.</summary>
    public class SendInputSink : GestureEngine.ISink {
        public void Scroll(int deltaY, int deltaX) { NativeInput.SendWheel(deltaY, deltaX); }
        public void GestureTriggered(GestureAction action) { NativeInput.SendGestureAction(action); }
        public void Zoom(int wheelDelta) { NativeInput.SendZoom(wheelDelta); }
        public void MouseButton(int button) { NativeInput.SendMouseButton(button); }
    }
}
