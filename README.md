# Magic Mouse Enhancer for Windows

[![Latest release](https://img.shields.io/github/v/release/saulatalibaig/magic-mouse-enhancer?style=flat-square&label=download)](https://github.com/saulatalibaig/magic-mouse-enhancer/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-informational?style=flat-square)](#requirements)

Enables **full Apple Magic Mouse functionality on Windows 11** (and Windows 10):
smooth one-finger scrolling (vertical + horizontal), and two-finger swipe gestures —
no paid drivers, no admin rights required for the app itself.

The program reads the mouse's touch surface over HID and injects the
corresponding Windows input (mouse wheel / horizontal wheel / keyboard shortcuts),
exactly like a driver would — but as a small, quiet system-tray application.
On Bluetooth it layers on top of Apple's own kernel driver to add the gesture
features Windows lacks.

---

## Read this first — Bluetooth vs USB

The Magic Mouse only streams touch data after receiving a HID **feature report**
(`F1 02 01` on Magic Mouse 2). Windows **refuses feature reports on Bluetooth HID
devices from user-mode apps** (`ERROR_INVALID_FUNCTION`, win32 error 1 — a
documented platform limitation; see hidapi #348 and OSR forums). What this means:

- **USB cable connection → works immediately with no driver.** Plug the mouse
  into the PC with its charging cable (Lightning for MM2, USB-C for the USB-C
  model). Over USB the feature report succeeds and this app reads the touch
  surface directly — full gestures including vertical swipes.
- **Bluetooth connection → uses Apple's kernel driver.** Windows blocks the
  feature report from user mode, so the app ships with
  `install-apple-bt-driver.ps1`, which installs Apple's own signed
  `applewirelessmouse.sys` (from Apple Software Update / Boot Camp) as a HID
  lower filter — the same method the commercial Magic Utilities uses. The
  driver then provides native one-finger scroll (vertical + horizontal), and
  this app adds two-finger swipe → Back/Forward on top of the driver's events.

The app auto-detects the active mode (raw HID vs. driver) and logs it.

---

## Features

| Feature | Default |
| One-finger scroll (vertical + horizontal) | on |
| Two-finger swipe left -> **Back** (Alt+Left) | on |
| Two-finger swipe right -> **Forward** (Alt+Right) | on |
| Two-finger swipe up -> **Task view** (Win+Tab) | on |
| Two-finger swipe down -> **Show desktop** (Win+D) | on |
| **Pinch to zoom** (spread = zoom in, close = zoom out, Ctrl+wheel) | on (USB/raw-HID mode) |
| Pinch sensitivity 1-10 (higher = faster zoom response) | 5 |
| **Smart zoom** (double-tap/double-click → zoom) | on (Bluetooth + USB) |
| **Tap to click** (one-finger tap = click) | off (USB mode) |
| **Two-finger tap = middle click** | on (USB mode) |
| **Right third of surface = right click** | off (USB mode) |
| **Low-battery alerts** (tray notification) | on |
| **Virtual desktop navigation** (swipe → Ctrl+Win+←/→) | rebindable |
| Natural (macOS) scroll direction | off (Windows style) |
| Scroll sensitivity 1-10 | 5 |
| Smooth scrolling (finer wheel steps) | off |
| Start with Windows | off |
| Master enable/disable from the tray | on |
|---|---|
| Two-finger vertical scroll (1 finger on the surface, macOS style) | ✅ on |
| Two-finger horizontal scroll | ✅ on |
| Swipe left → **Back** (Alt+Left) | ✅ |
| Swipe right → **Forward** (Alt+Right) | ✅ |
| Swipe up → **Task view** (Win+Tab) | ✅ |
| Swipe down → **Show desktop** (Win+D) | ✅ |
| Natural (macOS) scroll direction | off (Windows style) |
| Scroll sensitivity 1–10 | 5 |
| Smooth scrolling (finer wheel steps) | off |
| Start with Windows | off |
| Master enable/disable from the tray | ✅ |

All settings are configurable from the tray icon → **Settings…** and are stored in
`HKCU\Software\MagicMouseEnhancer`.

Supports: **Magic Mouse (1st gen)**, **Magic Mouse 2**, **Magic Mouse 2 (USB-C)** —
over USB always; over Bluetooth when used with Apple's driver (see the note above).

---

## Install

**[Download the latest installer](https://github.com/saulatalibaig/magic-mouse-enhancer/releases/latest)**
(`MagicMouseEnhancer-Setup-1.2.0.exe`), then:

1. Run the installer — it installs per-user to `%LOCALAPPDATA%\Programs\MagicMouseEnhancer`, so no admin rights are needed.
2. Pair your Magic Mouse via **Settings → Bluetooth & devices** (hold the mouse's power switch until it blinks).
3. The app appears in the tray; a balloon confirms when your mouse is detected.

Every release also publishes a `SHA256SUMS.txt`, so you can verify the download:

```powershell
Get-FileHash .\MagicMouseEnhancer-Setup-1.2.0.exe -Algorithm SHA256
```

> **Note:** the installer is not code-signed yet, so Windows SmartScreen shows
> *"Unknown publisher"* on first run — choose **More info → Run anyway**. See
> [SIGNING.md](SIGNING.md) for how releases will be signed.

Uninstall: Start menu → **Uninstall Magic Mouse Enhancer**, or
Settings → Apps → Installed apps.

---

## Usage

- **Scroll**: slide one finger on the touch surface. Direction and speed are
  configurable.
- **Swipe gestures**: two fingers, quick swipe past the threshold (~5 mm).
- **Tray icon** (right-click):
  - *Enabled* — turn gesture processing on/off instantly
  - *Settings…* — all options
  - *Exit* — stop the app (it does not auto-run until you enable *Start with Windows*)

## Settings explained

- **Natural scrolling** — macOS style: content follows your finger. Off =
  conventional Windows wheel direction.
- **Let Windows handle scrolling** — use this only if Windows itself already
  scrolls your Magic Mouse natively (it generally does not); the app then only
  provides gestures.
- **Smooth scrolling** — sends finer wheel steps (60 units) instead of standard
  detents (120). Browsers and modern apps scroll more smoothly.
- **Scroll sensitivity** — 1 (slow) to 10 (very fast).
- **Swipe actions** — rebind any direction to: Back, Forward, Task view,
  Show desktop, Page up/down, Home, End, or None.

---

## How it works

Two modes, auto-selected at runtime:

**Raw-HID mode (USB)** — no driver needed: enumerates HID devices (SetupAPI),
opens the vendor-defined touch collection, sends the multitouch feature report
(`F1 02 01` MM2 / `D7 01` MM1), decodes touch reports byte-for-byte from the
Linux kernel's `hid-magicmouse.c` format (report IDs 0x29 / 0x12), and converts
one-finger motion into injected wheel events and two-finger swipes into
keyboard shortcuts via `SendInput`. Full gesture set including vertical swipes
**and pinch-to-zoom** (the engine tracks the distance between the two touches;
spreading fires Ctrl+wheel up, closing fires Ctrl+wheel down — the universal
zoom shortcut in browsers, Office, PDF viewers and maps).

**Driver mode (Bluetooth)** — Apple's kernel driver owns the device and
converts touch into standard wheel / horizontal-wheel events. The app installs
a low-level mouse hook (`WH_MOUSE_LL`) that recognizes the two-finger swipe
signature — a short horizontal-wheel burst (2–6 notches in under ~500 ms,
followed by a pause) — and fires the configured gesture (Back / Forward).
Scroll sensitivity and natural-direction scaling apply to the wheel stream.
Vertical swipe gestures are only available in raw-HID (USB) mode, because the
driver cannot distinguish them from one-finger vertical scroll.

## Troubleshooting

| Symptom | Fix |
|---|---|
| No scroll / gestures at all | Make sure the tray balloon says *Connected*. Check the log: `%LOCALAPPDATA%\MagicMouseEnhancer\log.txt`. Over Bluetooth, install the Apple driver first (`install-apple-bt-driver.ps1`, as admin). |
| Scroll works but swipes don't (Bluetooth) | The two-finger swipe is detected as a short horizontal-wheel burst. Make it deliberate and quick (under ~0.5 s). If it still fails, re-run the gesture probe and check the log. |
| Double scrolling | Your setup may scroll natively — enable *Let Windows handle scrolling*. |
| Scroll direction feels wrong | Toggle *Natural scrolling* in Settings. |
| Gestures too sensitive | Make deliberate two-finger swipes; the threshold is ~2–6 wheel notches in under 500 ms. |

## Self-test

```
MagicMouseEnhancer.exe --selftest
```

Runs 22 offline checks (report decoding against hand-computed HID vectors,
scroll math, swipe detection, the event-gesture classifier, settings
round-trip and HID enumeration) and exits 0 on success.math, gesture logic, settings persistence, HID enumeration, input
marshaling) and writes `selftest-results.txt` next to the log. Exits 0 when
everything passes.

---

## Touch data decoding

The Magic Mouse only reports touch data after being switched into multitouch
mode via a HID **feature report** — `F1 02 01` on Magic Mouse 2 / USB-C,
`D7 01` on the 1st generation. The app does this automatically on connect
(the same reports the Linux kernel driver sends), then decodes the touch
reports:

| Device | Touch report id | Layout |
|---|---|---|
| Magic Mouse 1 | `0x29` | 6-byte prefix + N×8 bytes touch data |
| Magic Mouse 2 / USB-C | `0x12` | 8-byte mouse report, or 14-byte prefix + N×8 bytes touch data |

Byte-level decoding mirrors the Linux kernel driver
(`drivers/hid/hid-magicmouse.c`): 12-bit signed X/Y per touch, tracking id,
contact size, and the touch-state nibble (`0x30` start / `0x40` drag).

```
Magic Mouse ──HID──▶ DeviceReader (per interface) ──▶ TouchDecoder
                                                          │
                                                          ▼
Windows input ◀──SendInput── GestureEngine (scroll math + swipe FSM)
```

- **1 finger** moving → mouse wheel / horizontal wheel (sensitivity + natural
  direction applied, fractional notches accumulated for smoothness)
- **2 fingers** → swipe gesture state machine (threshold ~500 touch units,
  fires once per sequence, suppresses scroll)

Clicks and pointer movement are left to Windows' own HID mouse driver — the app
only *adds* scroll and gesture input, so nothing existing breaks.

---

## Building from source

No Visual Studio, no .NET SDK, no NuGet — it compiles with the C# compiler that
ships inside Windows (.NET Framework 4.8, present on every Windows 10/11).

```powershell
powershell -ExecutionPolicy Bypass -File gen-icon.ps1   # assets\app.ico
powershell -ExecutionPolicy Bypass -File build.ps1      # bin\MagicMouseEnhancer.exe
# then compile the installer (Inno Setup 6):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\setup.iss
```

### Layout

```
MagicMouseEnhancer/
├─ src/
│  ├─ Program.cs          entry point, single-instance, --selftest / --settings
│  ├─ HidApi.cs           HID + SetupAPI P/Invoke
│  ├─ MagicMouse.cs       device IDs (05AC/004C, 030D/0269/029F)
│  ├─ DeviceReader.cs     open interface, send MT feature report, read loop
│  ├─ TouchDecoder.cs     raw report → touch points (kernel-format faithful)
│  ├─ GestureEngine.cs    1-finger scroll / 2-finger swipe state machine
│  ├─ NativeInput.cs      SendInput: wheel, HWHEEL, keyboard combos
│  ├─ Settings.cs         registry-backed settings + autostart
│  ├─ TrayIcon.cs         tray menu + connect balloon
│  ├─ SettingsForm.cs     settings window (code-built)
│  └─ SelfTest.cs         15 offline checks
├─ installer/setup.iss    Inno Setup script
├─ build.ps1 / gen-icon.ps1
└─ bin/  dist/  assets/
```

## Limitations

- Decoding is implemented faithfully from the Linux kernel driver's documented
  report format and verified with hand-computed report vectors; final
  calibration with a physical mouse on your machine is recommended (the
  settings panel + `log.txt` make this easy).
- Touch *calibration* differences between mouse revisions are handled by the
  sensitivity setting.
- The first-gen Magic Mouse uses Bluetooth Classic; the Magic Mouse 2 uses
  Bluetooth LE — both are covered.
