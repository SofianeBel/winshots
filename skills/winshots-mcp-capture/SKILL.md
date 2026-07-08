---
name: winshots-mcp-capture
description: Use when Codex needs a local Windows screenshot, UI Automation text, or capture artifact from Winshots through its MCP server; especially for debugging a visible desktop app, targeting a specific window, attaching screenshot/context to a coding task, or inspecting recent captures. Requires Winshots MCP tools such as list_windows, capture_window, capture_active_window, list_recent_captures, and read_capture_context.
---

# Winshots MCP Capture

## Overview

Use Winshots for one-off local captures of Windows app state. It writes screenshots, UI context, and metadata to disk; do not imply the app uploads captures.

## Capture Workflow

1. Confirm the target:
   - If the user names an app, page, or process, call `list_windows` with `titleContains` or `processName`.
   - If the user says the current or focused window is the target, use `capture_active_window`.
   - If multiple windows match and the target is unclear, ask instead of guessing.
2. Capture the target:
   - Prefer `capture_window` with the `windowHandle` returned by `list_windows`.
   - Keep `activateWindow` true unless foreground changes would disrupt the user's task.
   - Use a short `delayMs` of 250-1000 ms when the window needs time to settle.
3. Read only as much context as needed:
   - Use the returned `ContextPreview` for quick inspection.
   - Use `read_capture_context` when the preview is truncated or the task needs full extracted text.
4. Report artifact paths:
   - Include `ScreenshotPath`, `TextPath`, and `MetadataPath` when they matter to the task.
   - Summarize visible state instead of dumping long context.

## Tool Map

- `list_windows`: discover capturable windows and handles.
- `capture_window`: capture a specific visible window by handle, title, or process.
- `capture_active_window`: capture the foreground window after the user has focused it.
- `list_recent_captures`: find previously saved local captures.
- `read_capture_context`: read `context.txt` by capture id or capture directory.

## Local Repo Checks

When editing Winshots itself, validate MCP capture behavior from the repo:

```powershell
dotnet build .\Winshots.slnx
.\scripts\smoke-mcp.ps1
.\scripts\smoke-mcp.ps1 -Capture
```
