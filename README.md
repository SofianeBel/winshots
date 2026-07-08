# Winshots

Winshots is a local Windows capture tool for Codex debugging.

It captures a Windows app screenshot plus best-effort Windows UI Automation text, stores everything locally, and offers manual capture, periodic capture, targeted MCP capture, and Codex-friendly visual debugging sessions.

Winshots is Windows-only and currently a V1 prototype.

## V1 Features

- Configurable global shortcuts:
  - Capture: `Ctrl+Shift+Space`
  - Capture to Codex: `Ctrl+Shift+Enter`
- Manual capture from the last non-Winshots active window
- Capture to Codex detects a running `codex.exe`, focuses the Codex chat composer instead of the integrated terminal, then attaches `screenshot.png`, `metadata.json`, and `context.txt`
- Periodic local timeline capture
- Visual session capture with contextual frames and optional `video.mp4`
- Capture timing metrics in `metadata.json`
- Top-right recording overlay while timeline/session capture is active or a capture is running
- Codex can list and target visible windows through MCP before taking a capture
- Local artifacts per capture:
  - `screenshot.png`
  - `context.txt`
  - `metadata.json`
- Recent capture browser with screenshot preview and extracted context
- MCP stdio server so Codex can call Winshots directly
- Smoke and measurement commands for CLI verification

No capture is uploaded by this app.

Shortcut settings are stored at:

```text
%APPDATA%\Winshots\settings.json
```

If Codex App is not already running, Windows refuses to focus it, or Winshots cannot safely identify the Codex chat composer, Winshots still saves the capture locally and reports the manual fallback path.

## Requirements

- Windows 10 or 11
- .NET 8 SDK
- Node.js and npm for the Electron review UI
- Optional: `ffmpeg` on `PATH` to create visual session `video.mp4` files
- Optional: Codex Desktop/CLI for Capture to Codex and MCP workflows

## Run

```powershell
dotnet run --project .\src\Winshots.App\Winshots.App.csproj
```

## Electron UI

```powershell
npm install
npm run ui:electron
```

The Electron UI reads the same local capture root as the C# app by default:

```text
%USERPROFILE%\Documents\Winshots\captures
```

Override it for testing with `WINSHOTS_CAPTURE_ROOT`.

Smoke-check without opening the full app window:

```powershell
npm run ui:smoke
npm run ui:screenshot
```

## Smoke Capture

```powershell
dotnet build .\Winshots.slnx
dotnet test .\Winshots.slnx --no-build
.\scripts\smoke-capture.ps1
.\scripts\measure-capture.ps1
.\scripts\smoke-mcp.ps1 -Session
.\scripts\smoke-mcp-real-examples.ps1 -OpenWebExamples
```

## Visual Sessions

From the app, use `Start session` / `Stop session`. From the CLI:

```powershell
dotnet run --project .\src\Winshots.App\Winshots.App.csproj -- record-session --duration-seconds 5 --interval-ms 1000
```

## Codex MCP Connection

Build once:

```powershell
dotnet build .\Winshots.slnx
```

Either run the explicit installer:

```powershell
.\scripts\install-codex-mcp.ps1
```

Or add this block to `%USERPROFILE%\.codex\config.toml`, replacing `<REPO_ROOT>` with this repository's absolute path using forward slashes, then open a new Codex thread:

```toml
[mcp_servers.winshots]
command = "dotnet"
args = ["<REPO_ROOT>/src/Winshots.Mcp/bin/Debug/net8.0-windows/Winshots.Mcp.dll"]
type = "stdio"
startup_timeout_sec = 20.0
```

The MCP server exposes:

- `list_windows`
- `capture_window`
- `capture_active_window`
- `list_recent_captures`
- `read_capture_context`
- `start_visual_session`
- `stop_visual_session`
- `list_visual_sessions`
- `read_visual_session_context`

Smoke-test the MCP server:

```powershell
.\scripts\smoke-mcp.ps1
.\scripts\smoke-mcp.ps1 -Capture
.\scripts\smoke-mcp.ps1 -Session
.\scripts\smoke-mcp-real-examples.ps1 -OpenWebExamples
```

For real examples, the smoke script opens YouTube and Twitter/X in browser windows when `-OpenWebExamples` is set. It captures Discord and Steam too when matching capturable windows are already open, or when `-LaunchDesktopApps` successfully starts them.

## Codex Plugin

This repo also ships a local Codex plugin for Winshots. It bundles the Winshots MCP server config and skills that tell Codex when to use targeted captures, active-window captures, recent capture context, and short visual sessions.

From the repo root, build the MCP server, then add the local marketplace and install the plugin:

```powershell
dotnet build .\Winshots.slnx
codex plugin marketplace add .
codex plugin add winshots@winshots-local
```

Open a new Codex thread after installing so the plugin skills and MCP server are loaded.

Default app captures are written to:

```text
%USERPROFILE%\Documents\Winshots\captures
%USERPROFILE%\Documents\Winshots\sessions
```

## Repository Status

This repository is intended to be published as source code for the local Windows prototype. Build outputs, captures, sessions, UI screenshots, `node_modules`, and local agent run state are ignored.

## License

All rights reserved. See [LICENSE](LICENSE).
