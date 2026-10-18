using System;
using System.IO;

namespace MagicMouseEnhancer {
    /// <summary>File logger writing to %LOCALAPPDATA%\MagicMouseEnhancer\log.txt</summary>
    public static class Log {
        static readonly object _lock = new object();
        static string _logPath = "log.txt";

        public static string LogPath { get { return _logPath; } }

        public static void Init() {
            try {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MagicMouseEnhancer");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, "log.txt");
            } catch { _logPath = "log.txt"; }
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        static void Write(string level, string msg) {
            try {
                lock (_lock) {
                    File.AppendAllText(_logPath, string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}",
                        DateTime.Now, level, msg, Environment.NewLine));
                }
            } catch { }
        }

        public static string ReadTail(int maxLines) {
            try {
                if (!File.Exists(_logPath)) return "(no log yet)";
                string[] all = File.ReadAllLines(_logPath);
                int start = Math.Max(0, all.Length - maxLines);
                return string.Join(Environment.NewLine, all, start, all.Length - start);
            } catch (Exception ex) { return "(log read failed: " + ex.Message + ")"; }
        }
    }
}
