---
name: winshots-agent-watch
description: Use when Codex must wait for a local Windows window, UI Automation text, visual change, disappearance, or visual stability through Winshots Agent Watch. Requires the Winshots MCP wait_for_window, wait_for_text, wait_for_change, wait_for_disappear, and wait_for_stable tools.
---

# Winshots Agent Watch

Use Agent Watch for deterministic local Windows waits. Always provide a bounded timeout and a title, process, or handle selector.

1. Use `wait_for_window` before interacting with an app that may still be launching.
2. Use `wait_for_text` only for Windows UI Automation text; never describe it as OCR.
3. Use `wait_for_change` for a material visual transition and `wait_for_stable` before inspecting a settled screen.
4. Use `wait_for_disappear` only when the condition must first be observed and then become absent.
5. Treat `Outcome` as authoritative. Report `Reason`, `DurationMs`, `FramesObserved`, and `AppliedBounds`, plus local artifact paths when present.

All screenshots, UI Automation context, hashes, and results stay on the local machine unless the user explicitly shares them.
