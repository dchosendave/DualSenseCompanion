# DualSense Companion

A companion app for the DualSense (PS5) controller on Windows. It emulates an Xbox 360 or DualShock 4 controller so you can play almost every PC game with your PS5 controller, and unlocks DualSense features like gyro aiming, adaptive triggers and the lightbar.

It has a full settings window styled after the white DualSense: a live view of your controller that lights up as you press buttons, plus pages for the lightbar, sticks, adaptive triggers, gyro, touchpad, button mapping and more. Changes apply instantly and are saved automatically.

Closing the window keeps the app running in the system tray so your controller keeps working. Click the tray icon to open the window again, or right-click it for quick actions and Exit.

## Features

**Controller emulation**
- Xbox 360 controller emulation (works with almost every game).
- DualShock 4 emulation with gyro, accelerometer and touchpad passed through natively (for games, emulators and Steam Input that support PlayStation controllers).
- USB and Bluetooth, DualSense and DualSense Edge.
- Automatic reconnect: unplug, switch between USB and Bluetooth, or let the battery die, and it picks the controller up again.
- Automatic hiding of the real controller (via HidHide) to prevent double input, safely restored on exit, crash, Ctrl+C or logoff.

**DualSense features**
- **Gyro aiming**: as mouse movement (best precision) or added to the right stick (works in every game). Always on, while holding a button, or toggled. Adjustable sensitivity, invert, turn/tilt axis, automatic drift correction and manual calibration.
- **Adaptive triggers**: soft, medium or hard resistance, weapon-style click, or vibration, separately for L2 and R2.
- **Lightbar**: custom color, battery level (green to red), set by the game (DualShock 4 mode) or off, with brightness control.
- **Player LEDs**: player 1-4 or all.
- **Battery level**: shown in the tray tooltip and menu; low-battery notification and red pulsing lightbar.
- **Mic mute button**: mutes/unmutes all Windows microphones, with the button LED showing the state.
- **Touchpad as mouse**: move with one finger, click = left click, two-finger click = right click, two-finger drag = scroll.
- **Rumble** with adjustable strength.

**Tuning**
- Stick deadzone, outer deadzone, anti-deadzone and response curve, per stick, with a live preview.
- Trigger deadzone.
- Button remapping.

**App**
- Settings window with live controller view, stick and trigger visualizers, live gyro readout and button mapping editor.
- System tray icon with quick actions; launching the app again just opens the window.
- Start with Windows.
- Dependency check on startup (asks before installing ViGEmBus / HidHide).

## Settings file

Everything can be changed in the app window. Settings are stored in `%AppData%\DualSenseCompanion\settings.json`; to edit the file by hand use **Settings > Open settings file**, save, then **Reload settings file**.

The file format:

```jsonc
"LeftStick": {
  "Deadzone": 0.05,       // ignore movement below 5% (stick drift)
  "OuterDeadzone": 0.02,  // reach 100% slightly before the edge
  "AntiDeadzone": 0.0,    // minimum output once you move (counters in-game deadzones)
  "Curve": 1.0            // 1 = linear, 2 = finer aim near the center
},
"TriggerDeadzone": 0.0,
"ButtonMap": {
  "Cross": "A",
  "Circle": "B",
  "Mute": "None"          // used by the mic feature unless that is turned off
}
```

`ButtonMap` keys (DualSense): `Cross, Circle, Square, Triangle, L1, R1, L3, R3, Create, Options, PS, DpadUp, DpadDown, DpadLeft, DpadRight, Touchpad, Mute`.
Values (virtual controller): `A, B, X, Y, LeftShoulder, RightShoulder, LeftThumb, RightThumb, Back, Start, Guide, Up, Down, Left, Right, Touchpad` (DualShock 4 only) or `None`. In DualShock 4 mode A/B/X/Y are Cross/Circle/Square/Triangle, Back is Share and Start is Options.

Gyro tuning in the file: `MouseSensitivity` (pixels per degree), `StickFullSpeed` (degrees per second for full stick), `StickAntiDeadzone`, `SmoothingThreshold`.

The log is written to `%AppData%\DualSenseCompanion\log.txt`.

## Building

Requires the .NET 8 SDK on Windows.

```
dotnet build
dotnet test
dotnet publish DualSenseCompanion -c Release -r win-x64 --self-contained false -o publish
```

## Roadmap

1. Support for up to 4 controllers.
2. Headphone jack support for game audio.

A huge thanks goes to:
1. ViGEm (Virtual Gamepad Emulation Driver) – For providing the essential Xbox 360 controller emulation.
2. HidHide – For allowing HID device filtering and hiding.
3. HidSharp – For handling HID device communication in C#.
4. Linux Kernel (hid-playstation.c) – For documentation on DualSense Controller.
5. NAudio – For Windows microphone control.
6. Nielk1's trigger effect documentation – For the adaptive trigger effect formats.
7. The awesome Open-Source Developers & Community Discussions – For shared knowledge on DualSense input/output handling.
