using System.Collections.Generic;

namespace MagicMouseEnhancer.Core {
    /// <summary>One finger contact reported by the touch surface.</summary>
    public class TouchPoint {
        public int Id;          // 0..15 tracking id
        public int X;           // signed touch coordinate (kernel convention, Y already negated)
        public int Y;
        public int Size;        // contact size hint
        public int TouchMajor;
        public int TouchMinor;
        public int Orientation;
        public byte RawState;   // touch state nibble (0x30 start, 0x40 drag, ...)
        public bool Down;       // RawState != 0
    }

    /// <summary>A decoded Magic Mouse HID input report.</summary>
    public class TouchReport {
        public byte ReportId;
        public int Clicks;      // button bits (informational — Windows handles clicks natively)
        public int RelX;        // pointer delta (informational)
        public int RelY;
        public readonly List<TouchPoint> Touches = new List<TouchPoint>();
    }
}
