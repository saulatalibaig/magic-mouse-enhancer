namespace MagicMouseEnhancer.Core {
    /// <summary>Actions that can be bound to two-finger swipe gestures.</summary>
    public enum GestureAction {
        None = 0,
        Back = 1,          // Alt+Left
        Forward = 2,       // Alt+Right
        TaskView = 3,      // Win+Tab
        ShowDesktop = 4,   // Win+D
        PageUp = 5,
        PageDown = 6,
        Home = 7,
        End = 8,
        DesktopLeft = 9,   // Ctrl+Win+Left — switch virtual desktop
        DesktopRight = 10, // Ctrl+Win+Right — switch virtual desktop
        BrowserBack = 11,  // XBUTTON1 (4th button)
        BrowserForward = 12 // XBUTTON2 (5th button)
    }

    public static class GestureActionNames {
        public static string Name(GestureAction a) {
            switch (a) {
                case GestureAction.None: return "None";
                case GestureAction.Back: return "Back (Alt+Left)";
                case GestureAction.Forward: return "Forward (Alt+Right)";
                case GestureAction.TaskView: return "Task view (Win+Tab)";
                case GestureAction.ShowDesktop: return "Show desktop (Win+D)";
                case GestureAction.PageUp: return "Page up";
                case GestureAction.PageDown: return "Page down";
                case GestureAction.Home: return "Home";
                case GestureAction.End: return "End";
                case GestureAction.DesktopLeft: return "Virtual desktop: left (Ctrl+Win+←)";
                case GestureAction.DesktopRight: return "Virtual desktop: right (Ctrl+Win+→)";
                case GestureAction.BrowserBack: return "Browser back (mouse 4th button)";
                case GestureAction.BrowserForward: return "Browser forward (mouse 5th button)";
                default: return a.ToString();
            }
        }

        public static GestureAction[] All {
            get {
                return new GestureAction[] {
                    GestureAction.None,
                    GestureAction.Back,
                    GestureAction.Forward,
                    GestureAction.TaskView,
                    GestureAction.ShowDesktop,
                    GestureAction.DesktopLeft,
                    GestureAction.DesktopRight,
                    GestureAction.BrowserBack,
                    GestureAction.BrowserForward,
                    GestureAction.PageUp,
                    GestureAction.PageDown,
                    GestureAction.Home,
                    GestureAction.End
                };
            }
        }
    }
}
