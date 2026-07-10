# Changelog

## 1.3.0 - 2026-07-10

### Added

- Agent Watch MCP tools: `wait_for_window`, `wait_for_text`, `wait_for_change`, `wait_for_disappear`, and `wait_for_stable`.
- Bounded outcome diagnostics with `succeeded`, `timed_out`, or `cancelled`, applied timeout/cadence/thresholds, duration, frames observed, comparisons, and local artifact paths.
- Deterministic tests for success, timeout, cancellation, text beyond the returned preview, prior-observation disappearance, visual change, stable duration, and cumulative drift.
- A real Windows Agent Watch smoke scenario that writes a JSON report and capture artifacts locally.

### Privacy

- Agent Watch uses only local window enumeration, Windows UI Automation, screenshots, capture context, and perceptual hashes. It adds no AI, OCR, telemetry, cloud service, or upload.

## 1.2.0 - 2026-07-09

- Added bounded local Instant Replay with event-aware retention, local session export, Electron controls, and MCP/CLI host commands.
