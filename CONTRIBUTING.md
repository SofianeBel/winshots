# Contributing

Winshots is a local Windows V1 prototype. Keep contributions small, verifiable, and aligned with the current product surface.

## Development Setup

Requirements:

- Windows 10 or 11
- .NET 8 SDK
- Node.js and npm for the Electron review UI
- Optional: `ffmpeg` on `PATH` for visual session videos

Install UI dependencies when working on the Electron surface:

```powershell
npm install
```

## Validation

Run the focused checks for the area you touched:

```powershell
dotnet build .\Winshots.slnx
dotnet test .\Winshots.slnx --no-build
```

For capture and MCP changes:

```powershell
.\scripts\smoke-capture.ps1
.\scripts\smoke-mcp.ps1
```

For Electron UI changes:

```powershell
npm run ui:smoke
```

## Contribution Rules

- Do not commit local captures, sessions, screenshots, build outputs, dependency folders, logs, or agent run state.
- Do not add network upload behavior for capture artifacts without making it explicit in the README and UI.
- Keep MCP tools bounded to the Winshots capture/session roots instead of arbitrary file access.
- Keep changes surgical: update only the files needed for the feature or fix.
