namespace MagicMouseEnhancer.Core {
    /// <summary>
    /// Decodes raw Magic Mouse HID reports into touch points.
    /// The byte layout mirrors the Linux kernel driver (drivers/hid/hid-magicmouse.c):
    ///   - Magic Mouse 1:  report id 0x29, 6-byte prefix + N*8 bytes of touch data
    ///   - Magic Mouse 2:  report id 0x12, 8-byte mouse report, or 14-byte prefix + N*8 bytes of touch data
    /// </summary>
    public static class TouchDecoder {
        public const byte REPORT_MM1 = 0x29;
        public const byte REPORT_MM2 = 0x12;

        public const byte STATE_MASK = 0xF0;
        public const byte STATE_NONE = 0x00;
        public const byte STATE_START = 0x30;
        public const byte STATE_DRAG = 0x40;

        /// <summary>Decode a raw report. Returns null when the report is not a recognisable Magic Mouse touch report.</summary>
        public static TouchReport Decode(byte[] data, int size) {
            if (data == null || size < 1) return null;
            TouchReport r = new TouchReport();
            r.ReportId = data[0];

            if (data[0] == REPORT_MM1) {
                // 6 bytes of prefix, then N*8 bytes of touch data
                if (size < 6 || (size - 6) % 8 != 0) return null;
                r.Clicks = data[3] & 3;
                r.RelX = ((data[3] & 0x0c) << 28 | (data[1] << 22)) >> 22;
                r.RelY = ((data[3] & 0x30) << 26 | (data[2] << 22)) >> 22;
                int n = (size - 6) / 8;
                for (int i = 0; i < n; i++) r.Touches.Add(DecodeTouch(data, 6 + i * 8));
            } else if (data[0] == REPORT_MM2) {
                if (size == 8) {
                    // plain mouse report without touch data
                    r.Clicks = data[1] & 3;
                    r.RelX = (data[3] << 24 | data[2] << 16) >> 16;
                    r.RelY = (data[5] << 24 | data[4] << 16) >> 16;
                } else if (size >= 14 && (size - 14) % 8 == 0) {
                    // 14-byte prefix + N*8 bytes of touch data
                    r.Clicks = data[1] & 3;
                    r.RelX = (data[3] << 24 | data[2] << 16) >> 16;
                    r.RelY = (data[5] << 24 | data[4] << 16) >> 16;
                    int n = (size - 14) / 8;
                    for (int i = 0; i < n; i++) r.Touches.Add(DecodeTouch(data, 14 + i * 8));
                } else {
                    return null;
                }
            } else {
                return null; // not a Magic Mouse touch report
            }
            return r;
        }

        static TouchPoint DecodeTouch(byte[] d, int o) {
            TouchPoint t = new TouchPoint();
            t.Id = (d[o + 6] << 2 | d[o + 5] >> 6) & 0xf;
            t.X = (d[o + 1] << 28 | d[o] << 20) >> 20;                       // signed 12-bit
            t.Y = -((d[o + 2] << 24 | d[o + 1] << 16) >> 20);                // signed 12-bit, negated (kernel convention)
            t.Size = d[o + 5] & 0x3f;
            t.TouchMajor = d[o + 3];
            t.TouchMinor = d[o + 4];
            t.Orientation = (d[o + 6] >> 2) - 32;
            t.RawState = (byte)(d[o + 7] & STATE_MASK);
            t.Down = t.RawState != STATE_NONE;
            return t;
        }
    }
}
