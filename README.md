# Winshots

Winshots is a local Windows V1 inspired by Codex Appshots and Chronicle-style context capture.

It captures the active window screenshot plus best-effort Windows UI Automation text, stores everything locally, and offers manual capture, periodic capture, and Codex-friendly visual debugging sessions.

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

## Run

```powershell
dotnet run --project .\src\Winshots.App\Winshots.App.csproj
```

## Smoke Capture

```powershell
.\scripts\smoke-capture.ps1
.\scripts\measure-capture.ps1
.\scripts\smoke-mcp.ps1 -Session
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

Or add this block to `C:\Users\sifly\.codex\config.toml`, then open a new Codex thread:

```toml
[mcp_servers.winshots]
command = "dotnet"
args = ["C:/Users/sifly/Documents/Winshots/src/Winshots.Mcp/bin/Debug/net8.0-windows/Winshots.Mcp.dll"]
type = "stdio"
startup_timeout_sec = 20.0
```

The MCP server exposes:

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
```

Default app captures are written to:

```text
%USERPROFILE%\Documents\Winshots\captures
%USERPROFILE%\Documents\Winshots\sessions
```
