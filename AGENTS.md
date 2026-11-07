# Repository Guidelines

## Project Structure & Module Organization

NEON is an x64 Windows 10/11 desktop widget written in C# and WPF. It uses the Windows .NET Framework 4.x compiler without external packages or a separate SDK.

- `src/`: application entry point, telemetry collectors, WPF windows, shared XAML styles, and Windows integrations. Consult `docs/architecture.md` before changing component responsibilities.
- `tests/`: C# verification classes and the PowerShell verification runner. Test sources compile into the application executable.
- `assets/`: icon sources, icon-generation script, and published screenshots.
- `docs/`: user guide, development instructions, troubleshooting, architecture, and roadmap.
- `bin/`, `artifacts/`, and `data/`: ignored executables, generated verification outputs, and local settings.

## Build, Test, and Development Commands

Run from the repository root in PowerShell:

```powershell
.\build.ps1
.\Start.cmd
.\tests\verify.ps1 -ExecutableName NeonMonitor-check.exe
```

The build creates `bin/NeonMonitor.exe` and embeds styles and icons. The launcher builds only when needed; rebuild explicitly after source changes. Verification builds an alternate executable, runs checks, and renders previews into `artifacts/`. Use an alternate name while NEON is running because Windows locks its executable.

## Coding Style & Naming Conventions

Use four-space indentation, Allman braces, PascalCase for types and methods, and camelCase for fields and locals. Match nearby code for compact helpers. Keep syntax compatible with the bundled .NET Framework compiler; avoid modern C# features requiring Roslyn. Use shared XAML styles and theme resources for controls. No formatter or linter is configured.

## Testing Guidelines

Tests use custom assertion-based verification rather than an external framework. Name test classes and files `FeatureTests`, following `StorageTests.cs`. Add checks for changed behavior and failure cases; no numerical coverage threshold is configured. Run the suite and inspect affected PNG previews. Validate Windows integration in a normal user context: restricted environments can hide processes or block WMI and DNS. Follow `docs/development.md` for manual Windows 10/11 acceptance. Real M-Lab tests transfer traffic and disclose the public IP; they are separate from routine verification.

## Commit & Pull Request Guidelines

Prefer concise imperative subjects with the recent `feat:`, `fix:`, or `docs:` prefixes. PRs should describe the resulting behavior, link relevant issues, report automated and manual verification, and include screenshots for UI changes. Update the user guide and architecture documentation when behavior changes. Keep generated outputs and local settings out of commits.
