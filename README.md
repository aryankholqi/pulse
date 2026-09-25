# Pulse — lightweight in-game overlay

GPU & CPU temperature and usage, RAM, VRAM, FPS, 1% low and a frame-time graph, on top of your game.

## For users: install

1. Run `Pulse-Setup-x.y.z.exe`.
2. If Windows shows **"Windows protected your PC"**, click **More info → Run anyway** (the app isn't code-signed).
3. Click **Next**, choose whether you want a desktop shortcut and whether Pulse should start when you sign in, then **Install**.
4. Leave **Launch Pulse now** checked and click **Finish**.

Pulse lives in the system tray (the pulse icon near the clock).

| Hotkey | Action |
|---|---|
| `Ctrl+Shift+O` | Show / hide |
| `Ctrl+Shift+L` | Full ⇄ compact |
| `Ctrl+Shift+P` | Next screen corner |

Set games to **Borderless / Windowed Fullscreen** so the overlay can appear on top.
Uninstall from **Settings → Apps → Pulse**.

## For developers: build the installer

**On your PC** (Windows):

```powershell
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.0.0
```

Output: `dist\Pulse-Setup-1.0.0.exe`. The script downloads PresentMon automatically.

**On GitHub** (no local tools needed): push the project to a GitHub repo, then

```powershell
git tag v1.0.0
git push --tags
```

GitHub Actions builds the installer and attaches it to a Release. You can also run the workflow manually from the **Actions** tab and download the installer from the run's artifacts.

## Why it doesn't slow games down

- WPF software rendering: the overlay never touches the GPU.
- No injection: FPS comes from ETW (PresentMon), so anti-cheat is not involved.
- Efficiency mode + below-normal priority.
- Sensors 1×/s, UI 2×/s, unchanged values are never redrawn; hidden overlay = no polling.

## Limits

- Exclusive Fullscreen hides the overlay; use Borderless.
- A non-injected overlay can move Windows from independent flip to composed mode (≈ one frame of latency, FPS usually unchanged). Use compact or hidden mode in competitive games if you care.
- CPU temperature needs LibreHardwareMonitor's driver; Defender or Memory Integrity may block it. Everything else still works.
- Primary monitor only.
