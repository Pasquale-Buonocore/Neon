# NEON documentation

This folder is the documentation home for NEON. Start with the page that matches your task:

| Page | What it covers |
| --- | --- |
| [User guide](user-guide.md) | Readings, tabs, controls, personalization, sensors, and startup |
| [Troubleshooting](troubleshooting.md) | Missing readings, launch, settings, and startup problems |
| [Development and verification](development.md) | Build commands, test modes, generated artifacts, and manual checks |
| [Architecture](architecture.md) | Runtime flow, component responsibilities, source files, and tests |
| [Roadmap](roadmap.md) | Current boundaries and proposed work |
| [Speed test feasibility](speedtest-feasibility.md) | Engine choice and live verification of the M-Lab speed test |

For installation, download or clone the repository to a writable folder on 64-bit Windows 10/11 and run `Start.cmd` from the repository root. The root `README.md` gives the shortest introduction; the [user guide](user-guide.md) has the full instructions.

## Open as an Obsidian vault

1. Clone or download the whole NEON repository to a writable folder. Paths containing spaces are supported.
2. In Obsidian, choose **Open folder as vault** and select this `docs` folder.
3. Open this `README.md` from the file list. The links use standard relative Markdown paths, so the same navigation works on GitHub.

No community plugins or shared Obsidian configuration are required. Obsidian may create a local `.obsidian` directory inside `docs`; Git ignores it, so your layout and preferences stay on your computer. The graph view is optional. Pages link to one another to make it useful, but all essential information is in the pages themselves.

## Documentation conventions

- Keep instructions in English and describe behavior implemented in the current code.
- Use relative Markdown links. Put images or other attachments used by these pages inside `docs` so they remain inside the vault.
- Keep the [architecture map](architecture.md) and [verification guide](development.md) aligned with source changes. Link to the relevant code and tests instead of duplicating implementation details.
