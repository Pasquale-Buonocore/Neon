# User guide

NEON is a 64-bit Windows 10/11 desktop widget. Download or clone the repository into a writable folder and double-click `Start.cmd` from its root. The launcher builds the executable on first run and starts it directly thereafter; .NET Framework 4.x is required, while Python, Node, and a separate SDK are not. This guide covers the full interface; see [troubleshooting](troubleshooting.md) when a reading or setting is unavailable.

## Monitor, Processes, and Storage

NEON starts on **Monitor**. Choose another tab from the tab row or the **Tabs** menu. The selection lasts for the current session; restarting NEON returns to Monitor. Focused tab buttons support Left/Right, Home/End, and Enter/Space. Monitor keeps collecting its 60 in-memory chart samples while another tab is selected. No monitoring history is saved to disk.

| Area | Reading |
| --- | --- |
| CPU | Total usage, processor model, and logical thread count |
| RAM | Usage percentage and used/total physical memory |
| GPU | Usage, model, and VRAM when a provider exposes them |
| Temperatures | Highest available CPU and GPU sensor readings |
| Network | Combined download and upload rates across active interfaces, in B/s, KiB/s, or MiB/s |
| Drives | Fixed local volumes, each with used space, capacity, and a usage bar; SYS marks the system drive |

Hover over the CPU and GPU cards for model names. Readings refresh at the selected interval, one second by default; temperature providers are queried no more than once every five seconds. `N/A` means a measurement is unavailable, never zero or an estimate.

**Processes** displays total PC CPU and RAM above the top eight running processes. Sort by **CPU** or **Memory**. Each row shows name, PID, CPU percentage, and physical memory; long names have a full-name tooltip. Process CPU is a percentage of the PC's total logical processor capacity. Its first sample is `N/A`, and the list temporarily ranks by memory while CPU measurement is primed. Inaccessible metrics show `N/A`. Disk activity is omitted because the available process I/O counters are not specific to disk.

**Storage** currently shows a placeholder. It does not scan, rank, or clean up files.

The shared header shows the system status in every tab. **SYSTEM RUNNING** means no monitored alert is active. It reports unavailable readings and fixed warning thresholds: CPU or RAM at 90%, CPU temperature at 90 °C, GPU temperature at 85 °C, and drive usage at 95%. The warning clears when the condition clears. These are general indicators, not hardware diagnostics.

The drive list represents volumes with drive letters rather than physical devices. Multiple partitions on one device may appear separately. Inaccessible volumes remain listed as `N/A`; removed volumes disappear on the next refresh. Long lists can be scrolled. Removable media and network drives are excluded.

Network rates sum active interfaces; VPNs and virtual adapters can cause traffic to be counted more than once. Uptime may include a previous session when Windows Fast Startup is enabled. NEON does not issue periodic sound or popup alerts.

## Position and controls

Drag **NEON / SYS** to move the widget. Its default position is the top-right corner of the available desktop. Moving it elsewhere saves a custom position; moving it back near that corner restores docking. Switching tabs preserves the panel's size and anchor.

NEON stays at desktop level behind other application windows. Clicking or revealing it does not bring it to the foreground. It has no taskbar entry. A second launch reveals the existing instance rather than starting another one.

Click **−** to hide the widget and double-click the notification-area icon to reveal it; Windows may put the icon in its hidden-icons overflow. Click **×** or choose **Quit NEON** to exit. Right-click the widget or notification-area icon for the same menu, including themes, background, sensor information, and tabs.

Choose **Internet speed test…** from that menu for an on-demand M-Lab test. Its companion window opens beside the widget with the same dimensions and follows it when moved or resized, shares its theme and opacity, and has no separate taskbar icon. Personalize and Speed Test replace each other; opening Personalize while a test is active cancels it. On a small display, the content scrolls inside the window. Read the traffic and data-policy notice before clicking **Start speed test**; opening the window does not start a test. M-Lab collects your public IP and publishes measurement data. Download and upload each transfer at most about 100 MiB of application data plus network overhead, or run for up to five seconds. The tachometer shows the active phase; completed results show download and upload in Mbps, the server, and the time. Latency uses the server's minimum TCP round-trip time when available and otherwise shows `N/A`. Cancel stops the current phase and does not present partial data as a complete test. If M-Lab cannot be found over DNS, the window says that no measurement was obtained. The passive network readings on Monitor remain separate, though they may reflect the test traffic while it runs.

## Personalize

Open **gear → Personalize…**. The settings window appears beside the widget and remains at desktop level. Opening it again reuses the existing window. Controls preview their changes immediately, including while resizing. Click **Save** to persist changes; **Cancel**, ×, or Esc restores the saved settings. **Reset** previews the defaults until you save.

| Setting | Range or behavior |
| --- | --- |
| Widget size | 60–150%; default 100% keeps the original half-desktop-height limit |
| Text size | 80–140%, independently of widget width; default 100% |
| Color theme | Matrix, Ice, Cyberpunk, Amber, Crimson, or Ghost |
| Background opacity | 0–95%; default 60%; text and chart lines remain visible at 0% |
| Refresh interval | 0.5–10 seconds; default one second |
| Launch at Windows login | Off by default; applied only on Save |

Custom size and text combinations may make the panel taller than half the desktop, but it stays within the available screen area. The settings window shares the widget's theme and background opacity. The gear menu also offers 0%, 35%, 60%, and 85% background presets. Themes change text, charts, borders, and backgrounds while preserving warning colors.

Settings are saved atomically to `data/settings.json` in the project folder. Missing or malformed files use defaults; older files retain known values and receive defaults for new controls. Position and docking are retained when other settings change. Old Compact and Pinned settings are ignored; NEON has one display mode.

### Windows login startup

The checkbox represents NEON's current-user Windows startup registration, which is read independently of `settings.json`. The status underneath reports a different executable path, a Windows startup block, or a state that cannot be verified. Reopening Personalize refreshes this status without discarding an unsaved selection.

Saving the enabled option registers the quoted absolute executable path. Disabling removes only NEON's startup value. No administrator privileges are required. If Windows disabled NEON in Startup apps or Task Manager, use **Open Windows startup settings** to re-enable it; NEON does not override that block. After moving the project folder, start the new copy and save the enabled option again to update the path. Commands over Windows' 260-character startup limit are rejected with an explanation.

If preference saving fails, the preview stays visible and the settings window explains the error; Cancel restores the last saved values. NEON attempts to restore the previous startup registration when a save fails and reports a restoration failure if one occurs. Startup uses the regular visible widget with saved theme, position, and docking.

## Temperatures and GPU sensors

Windows does not provide a universal CPU temperature reading. NEON uses these providers when available:

1. For the first NVIDIA GPU, NEON locates `nvidia-smi.exe` in the driver installation and reads usage, temperature, and VRAM when exposed.
2. For CPU temperature and other GPUs, run [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) with its WMI provider available. Some sensors require Libre Hardware Monitor to run as administrator. NEON does not install or start it.
3. The Open Hardware Monitor WMI provider is also supported.

Open **Sensor information** for provider notes. WMI and NVIDIA queries run in the background. Temperature is the highest available reading for a device and may come from a core, package, or hotspot sensor, depending on the provider. GPU metrics come from the first GPU exposed by the provider. If a provider stops responding, unavailable readings return to `N/A`; temperature is never inferred from utilization.

NEON requires no account or remote telemetry and does not change clocks, fans, or other hardware controls.
