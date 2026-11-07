# Development and verification

The [architecture map](architecture.md) identifies the main source files and their tests. NEON is a C# WPF application built with the .NET Framework 4.x compiler installed with 64-bit Windows 10/11. There are no external packages, Python, Node, or separate SDK requirements.

## Build and run

Run these commands from the repository root in PowerShell:

```powershell
.\build.ps1
.\tests\verify.ps1
```

The launcher `Start.cmd` builds `bin/NeonMonitor.exe` on first run and starts it directly thereafter. `build.ps1` compiles all C# files under `src/` and `tests/`, embeds the WPF styles and icon, and writes the executable under `bin/`. The icon source is `assets/neon.svg`; `assets/build-icon.ps1` regenerates the multi-size `assets/neon.ico`.

If NEON is running, Windows may lock its executable. Test with a different name:

```powershell
.\tests\verify.ps1 -ExecutableName NeonMonitor-check.exe
```

Close NEON before rebuilding the normal executable. Build outputs, logs, local settings, and generated test artifacts are ignored by Git.

## Automated checks

`tests/verify.ps1` builds the executable, runs its test and preview modes, and checks that each output exists under `artifacts/`. The modes include system telemetry, process sampling, Storage scanning, UI/tab behavior, desktop ordering, settings/startup, and rendered previews.

- Telemetry checks cover CPU calculations, network rates with real intervals, counter resets, interface changes, memory, uptime, and all fixed drives.
- Process checks cover first-sample `N/A`, normalization to total CPU capacity, sorting, process churn, PID reuse, inaccessible counters, session reset, and a real worker process sampled three times through launch and exit.
- UI checks cover scaling, small screens, tab navigation and stable size, chart history while Monitor is hidden, alerts, unavailable sensors, multiple/inaccessible/removed drives, themes, preferences, companion-window exclusivity, taskbar state, and active-test cancellation on close.
- Settings checks cover defaults, legacy files, malformed files, atomic saves, write failures, preview and Cancel behavior, font and widget scaling, refresh intervals, and docking.
- Startup checks cover provisional changes, Save/Cancel/×/Esc/Reset, external changes, Windows blocks, failed registration and preference saves, and rollback failures. Tests use a temporary key under `HKCU\Software\NEON\Tests` and clean up their subtree. UI tests and previews use a simulated startup service; the normal suite does not change the real Windows startup registration.
- Desktop checks create off-screen windows to verify ordering behind applications, clicks without activation, blocked topmost requests, showing from tray, and moving and minimizing.

The suite generates `artifacts/neon-preview.png`, `neon-themes.png`, `neon-tabs.png`, `neon-menu.png`, `neon-icon.png`, `neon-settings.png`, `neon-speed-test.png`, `neon-storage-details.png`, and `neon-storage-tab.png`. The Monitor part of the tab preview uses live readings; its Processes and Storage examples are illustrative. Synthetic data is not used in normal operation. The speed-test preview never starts a network test.

## Manual Storage check

Open Storage and confirm that no scan starts until **Start scan** is clicked. Select each local fixed volume, including two partitions on one disk if available; compare used/free/total with Windows. Check SSD/HDD only where physical disk identity is unambiguous. Build a disposable tree with nested large and small files, spaces in paths, a hard link, a junction or symbolic link, and a directory inaccessible to the current user. Scan it on a disposable volume or inspect the corresponding paths in a volume scan. Verify that each Details level mixes only direct children in descending size order, retaining up to 50 of each type. Check page boundaries, last pages, zero-byte filtering, and full tooltip text for paths and exclusion counts. At minimum, standard, and maximum widget/text scales, verify no Storage or Details scrollbars and no clipped controls. Compare Monitor, Processes, and Storage dimensions before and after populating Storage; hidden views must not enlarge the widget. Try canceling a scan and navigating the retained partial result, removing or disconnecting an available volume, closing and reopening Details while scanning, and replacing Details with Personalize and Speed Test. Monitor should continue to update; navigation should not start another scan. Check size, placement, theme, opacity, and no taskbar icon for Details. Repeat on Windows 10 and 11 before closing issue #11.

## Manual Internet speed test check

After reviewing M-Lab's data policy, open **Internet speed test…** from the widget menu and confirm that no traffic starts before clicking **Start speed test**. Check its position to the left of the widget (or right when space is insufficient), the same dimensions as the widget, desktop theme and opacity, scrolling on small displays, and absence from the taskbar. Move and resize the widget; the companion should follow. Start a test, open Personalize, and confirm that the test is canceled and only Personalize remains. Check tachometer updates and the separate download and upload readings. Each phase is limited to approximately 100 MiB of application data plus overhead or five seconds. Cancel during discovery, download, and upload; no canceled or failed run should display a complete result. The Monitor tab should continue updating during the test. For an app-level live probe without opening the window, run `bin\NeonMonitor.exe --speed-test artifacts\mlab-integration.json`; this command itself starts real download and upload traffic and shares the public IP with M-Lab. The regular verification suite does not run this network test. `--speed-cancel-test <report> finding|download|upload` and `--speed-failure-test <report> offline|timeout` provide targeted test modes; the timeout mode uses a local server that accepts a connection but does not answer. Run NEON as a normal user process for live network and process-list checks: restricted launch environments can hide other processes and block DNS independently of NEON. Repeat manual checks on Windows 10 and 11, including offline operation and network timeouts.

## Manual startup acceptance

Use a standard user account on Windows 10 and Windows 11. Run these checks from a project path containing spaces:

1. Save a theme and position or docking preference. Enable **Launch at Windows login**, save, sign out, and sign back in. Confirm one visible widget restores the settings and stays behind applications. Repeat with docked and custom positions.
2. Start the executable again while NEON is running. Confirm it reveals the existing widget without another instance.
3. Disable NEON in Windows Startup apps or Task Manager. Reopen Personalize and confirm the disabled status and Windows settings button. Save other settings, confirm the block remains, then re-enable it in Windows and refresh the status.
4. Uncheck startup and save. Confirm only NEON's registration disappears and no widget starts after the next sign-in.
5. Move the project folder, start the moved copy, and confirm the different-path status. Save the selected option again and confirm it points to the moved executable.

For documentation-only changes, also validate the relative links and vault steps in the [documentation index](README.md). The test suite does not replace the manual sign-in or Obsidian checks.
