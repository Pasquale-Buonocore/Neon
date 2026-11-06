# Roadmap

NEON currently has three tabs. **Monitor** shows live system and drive readings; **Processes** shows the top eight processes by CPU or memory. **Storage** is a placeholder, not a disk scanner. See the [user guide](user-guide.md) for the current feature set and the [architecture map](architecture.md) for implementation entry points.

The following are proposed additions or features awaiting final acceptance:

- Complete Storage with on-demand volume scanning, progress, cancellation, and clear partial-result states ([issue #11](https://github.com/Pasquale-Buonocore/Neon/issues/11)).
- Improve CPU temperature provider diagnostics and sensor identification ([issue #3](https://github.com/Pasquale-Buonocore/Neon/issues/3)).
- Allow GPU, local-volume, and network-interface selection ([issue #4](https://github.com/Pasquale-Buonocore/Neon/issues/4)).
- Add independent position lock and click-through controls ([issue #5](https://github.com/Pasquale-Buonocore/Neon/issues/5)).
- Validate the M-Lab speed test across Windows 10/11, network failures, cancellation, and limited connections before closing [issue #6](https://github.com/Pasquale-Buonocore/Neon/issues/6). Download, upload, an optional RTT reading, and a tachometer are implemented.
- Discover possible driver updates from Windows Update and official vendors without installing them ([issue #7](https://github.com/Pasquale-Buonocore/Neon/issues/7)).

Longer-term possibilities include direct sensor integration without a separately running provider and adjustable warning thresholds. The linked issues describe the intended scope; the code and [development guide](development.md) describe the current implementation.
