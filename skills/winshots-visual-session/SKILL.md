---
name: winshots-visual-session
description: Use when Codex needs a short local Winshots visual debugging session over time, including repeated screenshot frames, UI Automation context, and optional video; especially for reproducing transient UI bugs, animation/state changes, navigation flows, or interactions where a single screenshot is insufficient. Requires Winshots MCP tools such as start_visual_session, stop_visual_session, list_visual_sessions, and read_visual_session_context.
---

# Winshots Visual Session

## Overview

Use a visual session when the important evidence changes over time. Keep sessions short and local; screenshots, context, and optional video stay on disk under the Winshots session root.

## Session Workflow

1. Choose a target:
   - Use `list_windows` first when the user names an app, browser tab, or process.
   - Pass `windowHandle`, `titleContains`, or `processName` to `start_visual_session` when the target is known.
   - Omit target selectors only when the user has explicitly focused the right window.
2. Start the shortest useful session:
   - Default to `intervalMs` 500-1000.
   - Default to `maxDurationSeconds` 5-15 for interactive bugs.
   - Set `includeVideo` false unless video is explicitly useful or requested.
3. Stop the session:
   - Always call `stop_visual_session` for the returned session id.
   - If the tool process restarted or the id is unavailable, use `list_visual_sessions` to find recent saved sessions.
4. Read and summarize:
   - Use `read_visual_session_context` to inspect `context.md`.
   - Summarize the important frame sequence and include `DirectoryPath`, `ContextPath`, and `VideoPath` when present.

## Tool Map

- `start_visual_session`: begin repeated local frame and context capture.
- `stop_visual_session`: finalize `session.json`, `context.md`, `frames.jsonl`, and optional `video.mp4`.
- `list_visual_sessions`: find recent saved sessions.
- `read_visual_session_context`: read the Markdown session summary by id or directory.

## Guardrails

- Prefer `winshots-mcp-capture` for static UI state.
- Do not leave sessions running after the evidence has been captured.
- Avoid long recordings unless the user explicitly needs them.
- Do not claim captures are uploaded; Winshots stores them locally.

## Local Repo Checks

When editing Winshots session behavior, validate from the repo:

```powershell
dotnet build .\Winshots.slnx
.\scripts\smoke-mcp.ps1 -Session
```
