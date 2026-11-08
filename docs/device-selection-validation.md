# Device selection validation

Issue #4 is implemented in the dedicated Devices companion, telemetry, chart transitions, drive warnings and the Storage volume picker. The following records the evidence available on the development workstation; it does not claim acceptance on an untested Windows version or physical hot-plug hardware.

| Requirement | Evidence |
| --- | --- |
| Enumerate stable, readable devices | Live inventory discovers NVIDIA UUID and Windows PNP GPU entries, two volume GUIDs, and eight interface IDs. Devices without a stable volume identity remain automatic-only. |
| Persist choices and preserve preferences | Settings tests save/reload GPU, volume and interface identities; legacy migration, draft copies, Save/Cancel/Reset and persistence failure checks pass. |
| GPU readings stay on the selected card | Multi-GPU tests use distinct usage, temperature, VRAM and fan readings. UUID tests cover reordered/missing rows and unsupported metrics. Complete provider samples are published atomically. The live GTX 1060 SMI entry supplies readings; Windows entries without an unambiguous sensor association remain N/A. |
| Include/exclude individual volumes | Control tests select one of two volumes. Live checks select C: and E: separately and verify the intended mount. Monitor and Storage share the volume filter; warnings consume the filtered snapshot. |
| Choose one interface or aggregate explicitly | Control and counter tests cover individual selection, explicit aggregation, VPN exclusion, reset and reconnect baselines. Live checks select all eight interface entries individually: active Wi-Fi/virtual adapters expose counters, disconnected adapters remain N/A. |
| Removed devices never silently substitute another | Missing GPU/volume/interface checks exercise the live collector. Synthetic Storage UI checks retain an unavailable identity, recover it under a changed drive letter, and cancel a scan when its volume is excluded. |
| Apply to charts and UI | GPU/network changes clear affected history and immediately show N/A until new readings arrive. In-flight snapshots for old selections are discarded. Full UI, settings, desktop-ordering and preview verification passes. The themed device preview has been visually inspected. |

The full verification runner and an alternate executable are used while the running normal executable is locked. Generated reports and PNGs remain under ignored `artifacts/`.

Still required for final hardware acceptance: physical removal/reconnection during monitoring and scanning, comparison with vendor readings under load, and the manual checks in [development](development.md) on both Windows 10 and Windows 11. Only the current PC is available. Automated/synthetic coverage does not replace those checks. Issue #4 remains open pending that acceptance.
