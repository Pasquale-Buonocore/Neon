# Architecture

This map is for contributors and future agents. It records the current runtime path and where to make changes. Start with [development and verification](development.md) for build commands, the [user guide](user-guide.md) for visible behavior, and [troubleshooting](troubleshooting.md) for failure states.

```mermaid
flowchart LR
    A[App startup] --> W[MonitorWindow]
    W -->|background read| T[Telemetry]
    W -->|Processes tab| P[ProcessSampler]
    T --> S[System snapshot]
    P --> Q[Process snapshot]
    S --> W
    Q --> W
    W -->|manual Storage scan| C[StorageScanner]
    C --> R[In-memory folder tree, top 50 per level]
    R --> W
    W --> UI[Monitor, Processes, and Storage views]
```

At normal startup, `src/App.cs` establishes the single-instance mutex and reveal event, creates the WPF application, and opens `MonitorWindow`. Test and preview command-line modes branch before the normal UI startup. `MonitorWindow` owns the tabs, view layout, status, charts, menus, refresh timer, companion windows, docking, and tray interactions. It calls `Telemetry.Read()` on a background task and applies its snapshot on the UI thread. Process sampling runs on a background task while Processes is selected; Monitor chart samples continue when another tab is visible. Storage volume discovery and the manually started scan run on background tasks; the scan result remains in memory for the session.

## Component map

Paths below are repository-relative names for finding code from the cloned repository. They are text rather than links because an Obsidian vault rooted at `docs/` cannot navigate to files outside that folder.

| Responsibility | Source | Verification |
| --- | --- | --- |
| Entry point, single instance, test/preview modes | `src/App.cs` | `tests/verify.ps1` and mode checks in `src/App.cs` |
| WPF widget, tabs, charts, alerts, menus, refresh, settings orchestration | `src/MonitorWindow.cs` | UI test mode in `src/MonitorWindow.cs`; `tests/verify.ps1` |
| CPU, RAM, network, fixed volumes, GPU and temperature providers; snapshot data | `src/Telemetry.cs` | Self-test mode in `src/App.cs`; `tests/verify.ps1` |
| Per-process collection, CPU deltas, PID reuse handling, ranking | `src/ProcessTelemetry.cs` | `tests/ProcessTests.cs` |
| Local fixed-volume discovery, media identification, iterative scan, ranking, app-name lookup | `src/StorageTelemetry.cs` | `tests/StorageTests.cs` |
| Paged Storage companion window with mixed file/folder navigation | `src/StorageDetailsWindow.cs` | Storage UI checks and `storage-preview` in `tests/verify.ps1` |
| On-demand M-Lab download/upload test and companion tachometer window | `src/MlabSpeedTest.cs`, `src/SpeedTestWindow.cs`, `src/SpeedGauge.cs` | Manual `--speed-test` mode, `speed-preview`, and UI companion checks in `tests/verify.ps1` |
| Preferences, defaults, normalization, atomic JSON persistence | `src/Preferences.cs` | `tests/SettingsTests.cs` |
| Personalize and dedicated tabbed Devices companion, live preview | `src/SettingsWindow.cs`, `src/DesktopSlider.cs` | `tests/SettingsTests.cs` and UI test mode |
| Current-user startup registration and Windows approval state | `src/StartupRegistration.cs`, `src/WindowsStartupRegistry.cs` | `tests/StartupTests.cs` |
| Desktop-level window placement and order | `src/DesktopLayer.cs` | `tests/DesktopLayerTests.cs` |
| Color palettes, WPF styles, and embedded icon | `src/Theme.cs`, `src/*Styles.xaml`, `src/BrandIcon.cs`, `assets/neon.svg` | UI and preview modes in `tests/verify.ps1` |

## State and external boundaries

- `MonitorWindow` loads preferences through `PreferencesStore` from `data/settings.json`. The settings window previews changes immediately and saves only on Save; Cancel restores the saved values. The file is local and ignored by Git.
- Windows login startup registration is separate from JSON preferences. `WindowsStartupRegistration` reads and writes the current user's registry value through `WindowsStartupRegistry`; tests and previews use a simulated service. A blocked or unknown Windows approval state is reported, not overridden.
- System CPU and RAM use Windows APIs. Network rates use interface counters and elapsed time; drives reuse `StorageVolumeCatalog` for fixed volumes and stable Windows volume IDs. GPU and temperature information comes from supported NVIDIA or WMI providers. Unavailable readings remain unavailable rather than inferred.
- `DeviceSelection.cs` discovers readable GPU, volume and interface options off the UI thread. Preferences store a GPU UUID/PNP ID and nullable arrays of volume/interface IDs (null means automatic/all; empty means none). Copies clone arrays for Save/Cancel isolation. `Telemetry.Read(preferences)` samples a captured selection. The widget discards an in-flight result when its selection changed, clears affected charts, and excludes unselected volumes from warnings. Storage applies the same volume selection, preserves disconnected identities, and cancels a scan when its volume is excluded. `SensorReadings.cs` reduces complete provider samples atomically and keeps every GPU metric on one device. Explicit GPU selection binds NVIDIA rows by UUID or provider hardware by an unambiguous physical/name match; numeric provider indices are never persisted. Missing/ambiguous devices never trigger fallback. Rate baselines reset on selection changes, disconnections and counter resets.
- The widget remains behind normal applications through `DesktopLayer`. When its Explorer desktop layer is unavailable, it falls back to the bottom of the window order. It does not reparent Explorer windows.
- `SettingsWindow` also hosts the separate Devices companion, with GPU/Volumes/Network tabs, fixed readable width and a left-side anchor. It temporarily reserves space without changing saved docking preferences. `MenuDismissal` observes mouse button-down only while a context menu is open, checks native popup rectangles (including submenus), closes on outside clicks without consuming them, and releases its hook immediately when the menu closes. This works without activating the desktop-level widget.

When changing a visible feature, update the [user guide](user-guide.md) and its verification entry in [development](development.md). Record planned, unreleased behavior in the [roadmap](roadmap.md), not as current behavior.
