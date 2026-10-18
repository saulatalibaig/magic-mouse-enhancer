namespace MagicMouseEnhancer.Hid {
    /// <summary>Apple Magic Mouse device identifiers (from the Linux kernel hid-ids.h).</summary>
    public static class MagicMouse {
        public const ushort VENDOR_APPLE = 0x05AC;     // Apple USB vendor
        public const ushort VENDOR_APPLE_BT = 0x004C;  // Apple's Bluetooth vendor id

        public const ushort PID_MAGICMOUSE1 = 0x030D;      // Magic Mouse (1st gen, Bluetooth)
        public const ushort PID_MAGICMOUSE2 = 0x0269;      // Magic Mouse 2 (Lightning)
        public const ushort PID_MAGICMOUSE2_USBC = 0x029F; // Magic Mouse 2 (USB-C)

        public static bool IsMagicMouse(ushort vendorId, ushort productId) {
            if (vendorId != VENDOR_APPLE && vendorId != VENDOR_APPLE_BT) return false;
            return productId == PID_MAGICMOUSE1 ||
                   productId == PID_MAGICMOUSE2 ||
                   productId == PID_MAGICMOUSE2_USBC;
        }

        public static string DeviceName(ushort productId) {
            switch (productId) {
                case PID_MAGICMOUSE1: return "Apple Magic Mouse (1st gen)";
                case PID_MAGICMOUSE2: return "Apple Magic Mouse 2";
                case PID_MAGICMOUSE2_USBC: return "Apple Magic Mouse 2 (USB-C)";
                default: return "Apple Magic Mouse";
            }
        }
    }
}
