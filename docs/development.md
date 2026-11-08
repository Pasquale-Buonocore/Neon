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

## Device selection verification

Settings tests cover selection persistence, independent draft copies, all/none semantics, VPN exclusion, GPU identity matching and ambiguity, reindexing, multi-GPU sensor/UUID grouping, provider loss, removed GPUs, counter reset/reconnection, control preview/save, and Storage selection/removal/reconnection/cancellation. `device-settings-preview` renders a synthetic multi-device form to `artifacts/neon-device-settings.png`.

`device-test` reads the actual device inventory, selects each GPU, volume and interface in turn, and records availability in `artifacts/device-live-verification.txt`. It does not modify saved preferences, disable adapters, or start network transfers. Run it in a normal Windows user context. See [device selection validation](device-selection-validation.md) for the completed checks and remaining hardware acceptance.

Devices UI checks verify left-side companion placement, reuse, and preservation of the saved widget anchor. Menu tests open actual WPF popup/submenu windows, check that their screen rectangles remain interactive, dismiss from an outside screen point, and verify mouse-observer cleanup and reopening. Manually click the desktop and another application's window after opening the gear menu; the menu must disappear and the clicked target must still respond. Verify all three Devices tabs at normal and small widget sizes; header/navigation/footer stay visible while the device list scrolls.

On Windows 10 and 11, open gear → Devices…. Select each supported GPU and compare its model, load, temperature and VRAM against that device's provider; verify another card is never substituted if the selected card/provider disappears. Identical model names must remain unavailable when a unique association cannot be established. Select/exclude individual partitions and compare Monitor readings and warnings. Restart and verify identities persist; remove/reconnect selected devices, including a changed drive letter. Select a physical interface alone, a VPN/virtual adapter alone, and an explicit aggregate. Disconnect one aggregate member and verify N/A and chart gaps, then reconnect and check that counters do not spike. Test Save, Cancel and Reset, inventory refresh, small screens, themes, docking and scaling. Verify the same volume selection in Storage; exclude a scanning volume and confirm cancellation. Reconnect it under a different drive letter and verify its identity is retained.

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
