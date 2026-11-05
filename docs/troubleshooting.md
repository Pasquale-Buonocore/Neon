# Troubleshooting

Use the [user guide](user-guide.md) for controls and sensor-provider setup. For build and test commands, see [development and verification](development.md).

| Symptom | Check |
| --- | --- |
| Widget is not visible | Look in the notification-area overflow and double-click NEON's icon. The widget stays behind application windows. If NEON is already running, starting it again reveals the existing instance. |
| CPU temperature or GPU data says `N/A` | Open **Sensor information**. CPU temperature needs a supported WMI provider on many PCs; NVIDIA readings need an available driver tool. `N/A` can also mean the provider or sensor is unavailable. See [temperature setup](user-guide.md#temperatures-and-gpu-sensors). |
| A drive says `N/A` | The fixed volume may be unavailable or inaccessible. Removed volumes disappear at the next refresh. NEON does not substitute estimated capacity. |
| Network rate looks too high | Rates combine active interfaces. VPN and virtual adapters may count the same traffic more than once. |
| Changes cannot be saved | Put the repository in a folder your user can write to. Preferences live in `data/settings.json`; the settings window reports write failures. |
| NEON does not launch at login | Open **Personalize → Startup** and read the registration and Windows approval status. If Windows disabled it, use **Open Windows startup settings**. After moving NEON, save the startup option again to register the new executable path. |
| Build or startup fails | Use 64-bit Windows 10/11 with .NET Framework 4.x. Inspect `bin/neon-error.log` when present. Close a running NEON before replacing its executable, or build a separate test executable as described in the [verification guide](development.md#build-and-run). |

Windows approval data that NEON cannot recognize is shown as unverified. NEON never changes Windows' startup block itself. If the local registry provider denies access or is unavailable, Personalize reports the failure instead of claiming that startup was saved. A command longer than Windows' startup limit must be shortened by moving the project to a shorter path.

If you are investigating a code-level failure, begin with the [architecture map](architecture.md) to find the owning source file and its tests.
