# NEON // PC Monitor

A native desktop widget for 64-bit Windows 10/11, with a transparent background, a vertical panel with rounded corners, terminal typography and compact charts. The panel uses at most half the available desktop height and scales its width proportionally, accounting for Windows DPI scaling. Full mode shows CPU, RAM, GPU, temperatures, network traffic and local drives while you work or play.

## Getting started

Double-click **Start.cmd**, or run **bin/NeonMonitor.exe** after building the project. Python, Node and a separate SDK are not required. NEON uses .NET Framework 4.x and WPF, normally included with Windows 10/11.

Administrator privileges are not required. Launching a second instance shows the existing widget. The interface, messages and numeric formatting use English.

## Features

| Area | Readings |
| --- | --- |
| CPU | Total usage, processor model and logical thread count |
| RAM | Usage percentage, used/total physical memory and committed memory relative to the commit limit |
| GPU | Usage, model and VRAM when exposed by the provider |
| Temperatures | Highest available CPU and GPU sensor readings, refreshed every five seconds |
| Network | Combined download and upload rates across active interfaces, in B/s, KiB/s and MiB/s |
| Drives | All fixed local volumes, each with used space, capacity and a usage bar; SYS identifies the system drive |
| Session | Clock and time since the Windows kernel started |

- System readings refresh approximately once a second. Charts show the last 60 samples, kept only in memory. No monitoring history is saved to disk.
- Drag `NEON / SYS` to move the widget.
- By default, the panel sits exactly in the top-right corner of the available desktop area. Compact mode preserves its width, scale and corner anchor. Dragging it elsewhere saves a custom position; moving it back near the corner restores docking.
- The widget stays on the desktop, behind other applications. Clicking it, showing it from the tray or minimizing another window does not bring it to the foreground. It remains draggable, with settings accessible from the panel or notification-area icon.
- Use `=` to toggle compact mode. The previous always-on-top option has been removed, and legacy preferences enabling it are ignored.
- Full mode stays within half the desktop height. Hover over the CPU and GPU cards to see their model names.
- Open `···` or right-click for a dark menu with icons, checkmarks and submenus. Under `Background`, select full transparency or 35%, 60% or 85% opacity. Text and chart lines remain legible. Right-clicking the notification-area icon opens the same menu.
- Under `··· → Color themes`, choose **Matrix** (green), **Ice** (blue), **Cyberpunk** (purple and cyan), **Amber** (orange), **Crimson** (red) or **Ghost** (white). Themes change text, charts, borders and backgrounds while preserving warning colors. The selection is restored on the next launch.
- `−` hides the panel. Double-click the notification-area icon to show it. `×` closes the app.
- The NEON icon is a green N on a dark background, embedded in the executable, window and notification area. The ICO contains eight sizes from 16 to 256 pixels. Its vector source is `assets/neon.svg`, and `assets/build-icon.ps1` generates the icon reproducibly.
- Position, opacity, theme and display mode are saved in `data/settings.json` inside the project folder.
- Visual alerts indicate high temperatures (CPU at 90 °C, GPU at 85 °C), CPU/RAM usage at 90% and drive usage at 95%. These fixed thresholds are general indicators, not hardware diagnostics.
- No account, remote telemetry or cloud service is required. NEON does not change hardware clocks, fan settings or other hardware controls.

The drive list shows volumes with drive letters, rather than physical devices: multiple partitions on one device may appear separately. Inaccessible volumes remain listed as N/A, while removed volumes disappear on the next refresh. Long lists can be scrolled. Removable media and network drives are excluded.

## Temperatures and GPU sensors

Windows does not expose a universal CPU temperature reading. **N/A** means a reading is unavailable, not that its value is zero.

1. For NVIDIA GPUs, NEON looks for `nvidia-smi.exe` in the driver installation and reads usage, temperature and VRAM for the first GPU. No additional monitoring app is needed when the driver exposes these readings.
2. For CPU temperatures and other GPUs, run [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) with its WMI provider available. Some sensors require running **Libre Hardware Monitor** as administrator. NEON does not install or launch it automatically.
3. The Open Hardware Monitor WMI provider is also supported. Readings return to N/A when their provider becomes unavailable.

WMI and NVIDIA queries run in the background so the interface remains responsive. Temperatures are the highest available readings for each device, which may include core, package or hotspot sensors depending on the provider. GPU metrics come from the same device: the first GPU exposed by the provider. Temperatures are never estimated from utilization.

Network counters add up active interfaces, so VPNs and virtual adapters may count traffic on more than one interface. Uptime may span previous sessions when Windows Fast Startup is enabled. NEON does not issue periodic popups or sound notifications.

## Building and verification

```powershell
.\build.ps1
.\tests\verify.ps1
```

The build uses the .NET Framework compiler already installed with Windows. The application is written in C# with a WPF interface and requires no external packages.

Verification covers CPU calculations, network rates using actual intervals, counter resets, interface changes and live readings from all fixed drives. UI checks cover compact mode, stable scaling, corner placement, desktop mode, transparency, alerts, unavailable sensors, multiple/inaccessible/removed drives, themes and preferences. They also generate `artifacts/neon-preview.png`, `artifacts/neon-themes.png`, `artifacts/neon-menu.png` and `artifacts/neon-icon.png`. Synthetic samples are used only to exercise verification states, never during normal operation or in previews.

Desktop checks create off-screen test windows and verify native ordering behind other windows, clicks without activation, blocked topmost requests, showing from the tray, moving and minimizing the widget. NEON positions itself above Explorer's desktop and below applications; if that shell layer is unavailable, it uses the bottom of the window order. It does not reparent Explorer windows.

If NEON is running, build and test a separate executable to avoid the Windows executable lock:

```powershell
.\tests\verify.ps1 -ExecutableName NeonMonitor-check.exe
```

## Future work

Potential additions include direct sensor integration without an external provider, GPU selection and drive filtering, adjustable dimensions and warning thresholds, and an optional launch-at-login setting. This version does not configure automatic startup.
