# Changelog

## 1.3.1 - 2026-07-11

### Added

- Automatic local Windows OCR fallback when UI Automation exposes too little useful text, including Steam and other Chromium-based apps.
- OCR-backed `wait_for_text` and text-mode `wait_for_disappear` checks that keep polling images in memory.
- OCR source, language, timing, line, and character diagnostics in capture metadata and context artifacts.

### Changed

- Windows builds now target Windows 10 version 19041 or later for the native OCR APIs.

## 1.3.0 - 2026-07-10

### Added

- Agent Watch MCP tools: `wait_for_window`, `wait_for_text`, `wait_for_change`, `wait_for_disappear`, and `wait_for_stable`.
- Bounded outcome diagnostics with `succeeded`, `timed_out`, or `cancelled`, applied timeout/cadence/thresholds, duration, frames observed, comparisons, and local artifact paths.
- Deterministic tests for success, timeout, cancellation, text beyond the returned preview, prior-observation disappearance, visual change, stable duration, and cumulative drift.
- A real Windows Agent Watch smoke scenario that writes a JSON report and capture artifacts locally.

### Privacy

- Agent Watch uses local window enumeration, Windows UI Automation, screenshots, local Windows OCR fallback, capture context, and perceptual hashes. It adds no remote AI, telemetry, cloud service, or upload.

## 1.2.0 - 2026-07-09

- Added bounded local Instant Replay with event-aware retention, local session export, Electron controls, and MCP/CLI host commands.
