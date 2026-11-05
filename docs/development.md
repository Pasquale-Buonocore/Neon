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

`tests/verify.ps1` builds the executable, runs its test and preview modes, and checks that each output exists under `artifacts/`. The modes include system telemetry, process sampling, UI/tab behavior, desktop ordering, settings/startup, and rendered previews.

- Telemetry checks cover CPU calculations, network rates with real intervals, counter resets, interface changes, memory, uptime, and all fixed drives.
- Process checks cover first-sample `N/A`, normalization to total CPU capacity, sorting, process churn, PID reuse, inaccessible counters, and session reset.
- UI checks cover scaling, small screens, tab navigation and stable size, chart history while Monitor is hidden, alerts, unavailable sensors, multiple/inaccessible/removed drives, themes, and preferences.
- Settings checks cover defaults, legacy files, malformed files, atomic saves, write failures, preview and Cancel behavior, font and widget scaling, refresh intervals, and docking.
- Startup checks cover provisional changes, Save/Cancel/×/Esc/Reset, external changes, Windows blocks, failed registration and preference saves, and rollback failures. Tests use a temporary key under `HKCU\Software\NEON\Tests` and clean up their subtree. UI tests and previews use a simulated startup service; the normal suite does not change the real Windows startup registration.
- Desktop checks create off-screen windows to verify ordering behind applications, clicks without activation, blocked topmost requests, showing from tray, and moving and minimizing.

The suite generates `artifacts/neon-preview.png`, `neon-themes.png`, `neon-tabs.png`, `neon-menu.png`, `neon-icon.png`, and `neon-settings.png`. The Monitor part of the tab preview uses live readings; its Processes example is explicitly labeled illustrative. Synthetic process data is not used in normal operation.

## Manual startup acceptance

Use a standard user account on Windows 10 and Windows 11. Run these checks from a project path containing spaces:

1. Save a theme and position or docking preference. Enable **Launch at Windows login**, save, sign out, and sign back in. Confirm one visible widget restores the settings and stays behind applications. Repeat with docked and custom positions.
2. Start the executable again while NEON is running. Confirm it reveals the existing widget without another instance.
3. Disable NEON in Windows Startup apps or Task Manager. Reopen Personalize and confirm the disabled status and Windows settings button. Save other settings, confirm the block remains, then re-enable it in Windows and refresh the status.
4. Uncheck startup and save. Confirm only NEON's registration disappears and no widget starts after the next sign-in.
5. Move the project folder, start the moved copy, and confirm the different-path status. Save the selected option again and confirm it points to the moved executable.

For documentation-only changes, also validate the relative links and vault steps in the [documentation index](README.md). The test suite does not replace the manual sign-in or Obsidian checks.
