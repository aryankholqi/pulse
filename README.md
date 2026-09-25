# Pulse

**A lightweight performance overlay for Windows games that stays out of your way.**

Pulse shows your frame rate, frame times and hardware vitals in a small, clean card on top of your game. It measures FPS without injecting anything into the game, draws itself on the CPU so it never competes for GPU time, and goes quiet when you're not looking at it. You get the numbers without the overhead.

---

## Contents

- [Features](#features)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Customizing the overlay](#customizing-the-overlay)
- [Everyday use](#everyday-use)
- [Reading the overlay](#reading-the-overlay)
- [Why Pulse doesn't slow your games down](#why-pulse-doesnt-slow-your-games-down)
- [Known limitations](#known-limitations)
- [Troubleshooting](#troubleshooting)
- [Thanks](#thanks)
- [For developers](#for-developers)

---

## Features

### What it measures

| Metric | What you see |
|---|---|
| **FPS** | Live frame rate of the game you're playing, averaged over the last second |
| **1% low** | The frame rate of your slowest 1% of frames over the last 10 seconds, the best single number for stutter |
| **Frame time** | Average milliseconds per frame, plus a live frame-time graph |
| **GPU** | Temperature, usage, model name and VRAM used / total |
| **CPU** | Temperature, usage and model name |
| **RAM** | Memory used, percentage and total installed |

### Two layouts

- **Compact** (default): one slim line with FPS, GPU, CPU and RAM. It's easy to ignore until you need it.
- **Full**: a card with the frame-time graph, 1% low, usage meters, device names and VRAM details.

### Smart FPS tracking

- **Follows the game automatically.** Pulse tracks whichever window you're playing in, and it switches only once that window actually starts drawing frames.
- **Ignores interruptions.** A notification, launcher or chat window popping up doesn't steal the counter from your game.
- **Knows when a game is paused.** When a game stops presenting frames (loading, paused, minimized), the counter shows a dash instead of a misleading number.

### Color that means something

- **Temperatures change color** from cool teal to amber (72 °C and up) to red (85 °C and up), so you can spot thermal throttling at a glance.
- **FPS warnings** turn the counter amber below 60 FPS and red below 30. You can turn this off.
- **Your own colors** for the FPS, GPU, CPU and RAM labels: pick a preset swatch, use the color picker, or type a hex code.

### A customize window with a live preview

- See the overlay on a real game scene (*The Last of Us Part I & II*, *Cyberpunk 2077*, *Elden Ring*, *Red Dead Redemption 2*, *God of War Ragnarök*, *Counter-Strike 2*) or on **your own screenshot**.
- The preview uses your screen's real proportions and can show the game's own HUD, so you can check that the overlay doesn't cover your minimap or health bar.
- **Zoom** in to check the fine detail.

### Built to disappear

- **Six positions**: any corner, or top / bottom center.
- **Adjustable size** (80%–150%) and **background opacity** (30%–100%).
- **Click-through**: mouse clicks pass straight through the overlay to the game.
- **Never steals focus** and never shows up in Alt+Tab.
- **Global hotkeys** that work while the game is in focus.
- **Lives in the system tray**, with no taskbar clutter.

### Other features

- **Start with Windows**: runs quietly at sign-in with no UAC prompt each time.
- **English and فارسی (Persian)**, with full right-to-left layout for Persian.
- **Anti-cheat friendly**: FPS is read from Windows event tracing (ETW) through Intel PresentMon. Pulse doesn't inject into or hook the game.
- **Self-contained installer**: no .NET install is required.

---

## Installation

**Requirements:** Windows 10 (version 1809) or later, 64-bit.

1. Download `Pulse-Setup-x.y.z.exe` from the [Releases](../../releases) page.
2. Run it. If Windows shows **"Windows protected your PC"**, click **More info → Run anyway**. (Pulse isn't code-signed yet.)
3. Click **Next**, then choose your options:
   - **Create a desktop shortcut**
   - **Start Pulse automatically when I sign in**
4. Click **Install**.
5. Leave **Launch Pulse now** checked and click **Finish**.

> Pulse runs as administrator. It needs admin rights to read hardware sensors and to capture frame timings from Windows. The installer sets this up, so you won't see a UAC prompt every time Pulse starts at sign-in.

To upgrade, run the newer installer over the old one. Your settings are kept.

---

## Getting started

When you open Pulse, the **customize window** appears.

1. **Choose a layout**: *Compact* or *Full*.
2. **Choose a position** on the screen.
3. Adjust colors, size and background if you like. The preview on the right updates as you go.
4. Click **Launch overlay**.

The customize window closes and the overlay appears. Pulse keeps running in the **system tray** (the pulse icon near the clock).

5. Start your game in **Borderless** or **Windowed Fullscreen** mode. Pulse detects the game and starts counting frames.

That's it. The next time you start Pulse, it remembers everything.

---

## Customizing the overlay

To open the customize window at any time, **double-click the tray icon**, choose **Customize…** from its right-click menu, or run Pulse again.

### Layout
| Option | Best for |
|---|---|
| **Compact** | Playing: one unobtrusive line |
| **Full** | Benchmarking and tuning: graph, 1% low, meters and device details |

### Show on overlay
Switch **FPS**, **GPU**, **CPU** and **RAM** on or off to choose what the overlay shows, in both layouts. At least two must stay on, so the last two switches lock until you turn another one back on.

### Position on screen
Top left · Top center · Top right · Bottom left · Bottom center · Bottom right.
The overlay sits 16 px from the screen edge and repositions itself if you change resolution.

### Colors
For each of **FPS**, **GPU**, **CPU** and **RAM** you can:
- click one of the preset **swatches**,
- open the **color picker** to choose any color, or
- type a **hex code** such as `#7CC8FF`.

The **FPS warnings** switch controls whether FPS turns amber below 60 and red below 30. **Reset colors** restores the defaults.

### Appearance
- **Size**: scale the overlay from 80% to 150%.
- **Background**: set how solid the card looks, from 30% (see-through) to 100% (opaque).

### General
- **Start with Windows**: launch Pulse at sign-in. When it starts this way, it goes straight to the overlay without opening the window.
- **Show this window when Pulse starts**: turn this off to skip the customize window and show the overlay right away.

### Language
Switch between **English** and **فارسی** at the top of the window. The change applies right away, including in the tray menu. The overlay itself always uses short universal labels (*fps*, *GPU*, *1% low*).

### Preview
- Choose a **game scene** or click **Your screenshot…** to use an image of your own game.
- **Game HUD** turns the game's interface on or off in the preview, to check for overlap.
- **Zoom in** enlarges the corner where the overlay sits.

---

## Everyday use

### Hotkeys
These work from anywhere, even while the game has focus.

| Hotkey | Action |
|---|---|
| `Ctrl` + `Shift` + `O` | Show / hide the overlay |
| `Ctrl` + `Shift` + `L` | Switch between Full and Compact |
| `Ctrl` + `Shift` + `P` | Move to the next position |

If another app already uses one of these shortcuts, Pulse tells you which one at startup.

### System tray
| Action | Result |
|---|---|
| **Click** the icon | Show / hide the overlay |
| **Double-click** the icon | Open the customize window |
| **Right-click** the icon | Menu: Customize…, Show / hide, Compact mode, Position, Exit |

### Quitting
Right-click the tray icon and choose **Exit**, or click **Quit Pulse** in the customize window. Closing the customize window does **not** quit Pulse. It keeps running in the tray.

### Uninstalling
Go to **Settings → Apps → Installed apps → Pulse → Uninstall**. The uninstaller also removes the start-with-Windows task.

---

## Reading the overlay

```
 Compact:   144 fps   GPU 64° 97%   CPU 58° 41%   RAM 52%
```

- **fps**: current frame rate. A dash (**–**) means no game is drawing frames (on the desktop, loading, or paused).
- **1% low**: if this is much lower than your average FPS, you're feeling stutter even when the average looks fine.
- **Frame time graph**: a flat line is smooth. Spikes are hitches.
- **Temperatures**: 🟢 below 72 °C comfortable · 🟡 72–84 °C working hard · 🔴 85 °C and above, likely to throttle.
- **"Waiting for a game"**: Pulse is running but hasn't seen a game render yet.

---

## Why Pulse doesn't slow your games down

- **No GPU use.** The overlay is drawn with software rendering, so it never creates a GPU device or competes with your game for GPU time.
- **No injection.** FPS comes from Windows event tracing (PresentMon), not from hooking the game. This also keeps anti-cheat systems out of the picture.
- **Low priority.** Pulse runs at below-normal priority in Windows *efficiency mode*. On hybrid CPUs, that puts it on the efficiency cores.
- **Sparse updates.** Sensors are read once per second and the display updates twice per second. Values that haven't changed are never redrawn.
- **Idle when hidden.** When the overlay is hidden and the customize window is closed, Pulse stops reading sensors entirely.
- **Frees memory before you play.** The customize window is fully closed when you launch the overlay, and its preview images are released from memory.

---

## Known limitations

- **Exclusive Fullscreen hides the overlay.** Use **Borderless** or **Windowed Fullscreen**. Most modern games look and perform the same in borderless mode.
- **Possible small latency cost.** Any overlay that doesn't inject into the game can make Windows switch from "independent flip" to "composed" presentation. That adds about one frame of latency, though FPS usually stays the same. In competitive games where every millisecond counts, use Compact mode or hide the overlay.
- **CPU temperature may be unavailable.** Reading it needs the LibreHardwareMonitor driver, which Microsoft Defender or *Memory Integrity* (Core Isolation) can block. Every other reading still works.
- **Primary monitor only.**

---

## Troubleshooting

| Problem | Fix |
|---|---|
| The overlay doesn't appear over my game | Switch the game to **Borderless / Windowed Fullscreen**. Press `Ctrl+Shift+O` in case it's hidden. |
| FPS shows **–** while I'm playing | Click into the game window so it has focus. If the tray shows *"FPS off"*, reinstall Pulse so PresentMon is present, and make sure Pulse runs as administrator. |
| CPU temperature shows **–** | The sensor driver is being blocked (see [Known limitations](#known-limitations)). |
| A hotkey doesn't work | Another app has claimed it. Pulse lists the conflicting hotkeys at startup. Use the tray menu instead. |
| Opening Pulse again does nothing | Pulse is already running in the tray. Running it again opens the customize window. |

Settings are stored in `%APPDATA%\Pulse\settings.json`. If something goes wrong, details are written to `%APPDATA%\Pulse\error.log`.

---

## Thanks

These channels shared Pulse with their communities. Thank you!

- [**PC Gaming Hub**](https://t.me/pcgaminghub/28369) on Telegram

You'll find them in the app too, at the bottom of the customize window.

---

## For developers

Pulse is a .NET 8 WPF app. Sensor data comes from [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) and FPS data comes from [Intel PresentMon](https://github.com/GameTechDev/PresentMon).

### Build the installer locally (Windows)

```powershell
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.0.0
```

This produces `dist\Pulse-Setup-1.0.0.exe`. The script downloads the PresentMon console build automatically. For a manual setup, see [`Tools/README.txt`](Tools/README.txt).

### Build on GitHub

No local tools are needed. Push a version tag:

```powershell
git tag v1.0.0
git push --tags
```

GitHub Actions builds the installer and attaches it to a new Release. You can also run the workflow manually from the **Actions** tab and download the installer from the run's artifacts.

### Project layout

| Path | Purpose |
|---|---|
| `App.xaml.cs` | Startup, tray icon, hotkeys and the UI update loop |
| `Services/FpsService.cs` | Runs PresentMon and computes FPS, 1% low and frame times |
| `Services/SensorService.cs` | Reads CPU / GPU / RAM sensors through LibreHardwareMonitor |
| `OverlayWindow` / `OverlayCard` | The click-through overlay and its compact / full layouts |
| `SettingsWindow` | The customize window and live preview |
| `StartupTask.cs` | "Start with Windows" through Task Scheduler |
| `installer/Pulse.iss` | Inno Setup installer script |

Third-party licenses are listed in [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).
