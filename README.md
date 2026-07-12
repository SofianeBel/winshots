# Winshots

Winshots is an open-source, local-first Windows capture tool for Codex debugging.

It captures a Windows app screenshot plus best-effort Windows UI Automation text, falls back to local Windows OCR when accessibility text is too sparse, stores everything locally, and offers manual capture, periodic capture, targeted MCP capture, and browsable visual debugging sessions.

Winshots is Windows-only and currently a 1.3 local release.

## V1.3 Agent Watch

- Five bounded MCP waits for local Windows automation: `wait_for_window`, `wait_for_text`, `wait_for_change`, `wait_for_disappear`, and `wait_for_stable`
- Deterministic results distinguish `succeeded`, `timed_out`, and `cancelled`, with duration, frames observed, comparisons, reason, and every applied bound
- Standard MCP request cancellation is honored; timeouts clamp to 100-300000 ms and polling clamps to 100-5000 ms
- Text waits inspect full local Windows text context, using UI Automation first and local Windows OCR only as a fallback; only a bounded preview is returned
- Visual change/stability reuse the capture pipeline and perceptual dHash, while keeping screenshots, context, and metadata local
- Disappearance requires the target to have been observed first; stability is measured as a bounded duration against a fixed baseline

See [Agent Watch MCP reference and workflows](docs/agent-watch.md) for complete input/output schemas.

V1.3 retains the V1.2 Instant Replay features:

- Instant Replay keeps a local, circular buffer of recent useful screenshots (30 seconds by default, configurable from 5 to 120 seconds)
- Event-aware retention always keeps window/process/title changes; dHash removes low-value same-context duplicates, with periodic stable-screen keyframes
- Replay sampling yields quickly when another capture owns the shared capture gate and records busy/failure diagnostics instead of blocking manual capture
- The buffer is strictly bounded to at most 480 retained frames and 256 MB of frame artifacts; its `buffer.json` manifest has separately bounded frame/event lists
- `Save replay` atomically publishes autonomous frames, metadata, context, and `session.json` into the local Sessions library while the buffer remains reusable
- Electron provides a compact Instant Replay status/control banner in Sessions
- CLI and MCP replay commands control the single buffer owned by the running Winshots host through a validated local ephemeral descriptor
- No upload, telemetry, cloud service, or required ffmpeg

It also retains the V1.1 capture and visual session features:

- Configurable global shortcuts:
  - Capture: `Ctrl+Shift+Space`
  - Capture to Codex: `Ctrl+Shift+Enter`
- Manual capture from the last non-Winshots active window
- Capture to Codex detects a running `codex.exe`, focuses the Codex chat composer instead of the integrated terminal, then attaches `screenshot.png`, `metadata.json`, and `context.txt`
- Periodic local timeline capture
- Visual session capture with contextual frames and optional `video.mp4`
- Local Sessions library with frame timeline, screenshot preview, and associated context
- Bounded `WM_PRINT` capture with a virtual-screen fallback for compatibility
- Structured image strategy/fallback and UI Automation diagnostics in `metadata.json`
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

No capture is uploaded by this app. OCR runs through the Windows OCR engine on the local machine and depends on OCR languages installed for the current Windows profile.

Shortcut settings are stored at:

```text
%APPDATA%\Winshots\settings.json
```

If Codex App is not already running, Windows refuses to focus it, or Winshots cannot safely identify the Codex chat composer, Winshots still saves the capture locally and reports the manual fallback path.

## Requirements

- Windows 10 or 11
- .NET 8 SDK for source builds
- Node.js and npm for source Electron review UI work
- Optional: `ffmpeg` on `PATH` to create visual session `video.mp4` files
- Optional: Codex Desktop/CLI for Capture to Codex and MCP workflows

## Install Or Run Portable

To install Winshots like a normal Windows app, run:

```text
winshots-1.3.2-win-x64-setup.exe
```

The setup installs the Windows app, Electron review UI, MCP server, Start Menu shortcuts, and an Apps & Features uninstaller under:

```text
%LOCALAPPDATA%\Programs\Winshots
```

Codex plugin registration is intentionally separate so a locked Codex plugin cache cannot break the Winshots app install. After closing Codex, run this from the installed folder only when you want to refresh `winshots@winshots-local`:

```powershell
.\install.ps1 -InstallCodexPlugin
```

For portable use without installation, download and extract:

```text
winshots-1.3.2-win-x64.zip
```

Then run:

```powershell
.\Winshots Review UI.cmd
```

Build the installer package locally with:

```powershell
.\scripts\build-release.ps1 -Version 1.3.2
```

The release files are written to:

```text
artifacts\release\winshots-1.3.2-win-x64-setup.exe
artifacts\release\winshots-1.3.2-win-x64.zip
```

## Run

```powershell
dotnet run --project .\src\Winshots.App\Winshots.App.csproj
```

Use `--winforms` only to force the legacy WinForms fallback.

## Electron UI

```powershell
npm install
npm run ui:electron
```

The Electron UI calls the local C# app for capture, Capture to Codex, timeline, and visual session commands. It reads the same local capture and session roots as the C# app by default:

```text
%USERPROFILE%\Documents\Winshots\captures
%USERPROFILE%\Documents\Winshots\sessions
```

Override them for testing with `WINSHOTS_CAPTURE_ROOT` and `WINSHOTS_SESSION_ROOT`. Override the C# app command with `WINSHOTS_APP_PATH` when testing against a specific built executable.

Smoke-check without opening the full app window:

```powershell
npm run ui:smoke
npm run ui:test
npm run ui:screenshot
```

## Smoke Capture

```powershell
dotnet build .\Winshots.slnx
dotnet test .\Winshots.slnx --no-build
.\scripts\smoke-capture.ps1
.\scripts\measure-capture.ps1
.\scripts\smoke-mcp.ps1 -Session
.\scripts\smoke-mcp.ps1 -AgentWatch
.\scripts\smoke-agent-watch.ps1
.\scripts\smoke-mcp-real-examples.ps1 -OpenWebExamples
```

## Visual Sessions

From the app, use `Start session` / `Stop session`. From the CLI:

```powershell
dotnet run --project .\src\Winshots.App\Winshots.App.csproj -- record-session --duration-seconds 5 --interval-ms 1000
```

## Instant Replay

Start Winshots normally so the C# host owns the single local buffer. In Electron, open Sessions and use the Instant Replay banner. The same host can be controlled from the CLI:

```powershell
Winshots.App.exe instant-replay status
Winshots.App.exe instant-replay start --lookback-seconds 30 --interval-ms 1000
Winshots.App.exe instant-replay save --lookback-seconds 20
Winshots.App.exe instant-replay stop
```

The temporary buffer stays under `%LOCALAPPDATA%\Winshots\instant-replay`; saved replays are copied atomically under `%USERPROFILE%\Documents\Winshots\sessions`. All files remain local unless the user explicitly shares them.

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
args = ["<REPO_ROOT>/src/Winshots.Mcp/bin/Debug/net8.0-windows10.0.19041.0/Winshots.Mcp.dll"]
type = "stdio"
startup_timeout_sec = 20.0
tool_timeout_sec = 330
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
- `get_instant_replay_status`
- `start_instant_replay`
- `stop_instant_replay`
- `save_instant_replay`
- `wait_for_window`
- `wait_for_text`
- `wait_for_change`
- `wait_for_disappear`
- `wait_for_stable`

Smoke-test the MCP server:

```powershell
.\scripts\smoke-mcp.ps1
.\scripts\smoke-mcp.ps1 -Capture
.\scripts\smoke-mcp.ps1 -Session
.\scripts\smoke-mcp.ps1 -AgentWatch
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

Winshots is available under the [MIT License](LICENSE).
