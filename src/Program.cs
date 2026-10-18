using System;
using System.Threading;
using System.Windows.Forms;

namespace MagicMouseEnhancer {
    static class NativeMethods {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }

    static class Program {
        [STAThread]
        static int Main(string[] args) {
            Log.Init();
            Log.Info("Magic Mouse Enhancer starting (PID " + System.Diagnostics.Process.GetCurrentProcess().Id + ")");

            if (args != null && args.Length > 0 && args[0] == "--selftest") {
                return SelfTest.Run();
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "MagicMouseEnhancer.SingleInstance", out createdNew)) {
                if (!createdNew) {
                    MessageBox.Show("Magic Mouse Enhancer is already running — look for its icon in the system tray.",
                        "Magic Mouse Enhancer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                // High-DPI: crisp UI on Retina / high-DPI displays (Magic Utilities "Retina" feature).
                try { NativeMethods.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (AppController controller = new AppController()) {
                    using (TrayIcon tray = new TrayIcon(controller)) {
                        controller.Start();
                        if (args != null && args.Length > 0 && args[0] == "--settings") tray.ShowSettings();
                        Application.Run();
                    }
                }
            }
            return 0;
        }
    }
}
