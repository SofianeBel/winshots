# Agent Watch MCP reference

Agent Watch turns Winshots' local capture primitives into deterministic, bounded waits for Windows agents. It uses Windows UI Automation first and the local Windows OCR engine only when accessibility text is too sparse. It does not use remote AI, telemetry, a cloud service, or a second background daemon.

## Target selection

Every tool requires at least one target selector:

| Input | Meaning |
| --- | --- |
| `windowHandle` | Exact handle returned by `list_windows`, such as `0x1234`. |
| `titleContains` | Case-insensitive window-title substring. |
| `processName` | Case-insensitive process-name substring, with or without `.exe`. |

When multiple windows match title/process filters, selection is deterministic: foreground first, then process name, title, and numeric handle.

## Tools and inputs

All five tools accept `timeoutMs` (default 10000, clamped to 100-300000) and `pollIntervalMs` (default 500, clamped to 100-5000). Standard MCP request cancellation is explicit and produces a `cancelled` result after any bounded capture already in progress returns.

| Tool | Additional inputs | Success condition |
| --- | --- | --- |
| `wait_for_window` | None | A matching capturable top-level window exists. |
| `wait_for_text` | Required `textContains` | The case-insensitive substring exists in the full current local text context. Matching is performed before the response preview is truncated; local Windows OCR is used only when UI Automation is too sparse. |
| `wait_for_change` | `outputRoot`; `minHashDistance` default 5, clamped 1-64 | Current screenshot dHash differs from the fixed baseline by at least the applied Hamming distance. A replacement matching window counts as distance 64. |
| `wait_for_disappear` | Optional `textContains` | The matching window or UIA text is absent after it was observed at least once. Initial absence never succeeds. |
| `wait_for_stable` | `outputRoot`; `stableDurationMs` default 1500, clamped 100-60000; `maxHashDistance` default 2, clamped 0-64 | Every sampled screenshot remains within the applied dHash distance from the fixed stable baseline for the applied duration. A drift beyond the threshold resets both baseline and duration. |

`wait_for_change` and `wait_for_stable` reuse the normal capture workflow. Each sampled frame keeps `screenshot.png`, `context.txt`, and `metadata.json` under `outputRoot`, or under `%USERPROFILE%\Documents\Winshots\captures` by default. The visual predicates compare screenshots only. Text waits keep OCR probes in memory and do not create a capture directory for every poll.

`wait_for_text` and text-mode `wait_for_disappear` inspect live local text using UI Automation first and Windows OCR only when needed. The full bounded result is used for matching, but only `TextPreview` (at most 1000 characters) is returned.

## Result schema

Each tool returns one JSON object:

| Field | Meaning |
| --- | --- |
| `Condition` | Stable condition identifier such as `window_present` or `visual_stable`. |
| `Outcome` | Exactly `succeeded`, `timed_out`, or `cancelled`. |
| `Reason` | Human-readable success/timeout/cancellation cause. |
| `DurationMs` | Observed elapsed duration. |
| `FramesObserved` | Number of bounded observations attempted. |
| `Comparisons` | Number of dHash comparisons, zero for non-visual waits. |
| `AppliedBounds` | Effective clamped timeout, polling cadence, stable duration, and relevant hash thresholds. |
| `Target` | Echo of the requested selectors. |
| `TextContains`, `TextSource`, `TargetObserved` | Text/disappearance diagnostics when relevant. `TextSource` is `windows_ui_automation`. |
| `LastHashDistance`, `StableForMs` | Last visual comparison and measured stable duration when relevant. |
| `BaselineObservation`, `LastObservation` | Bounded window diagnostics and local artifact paths. Text is preview-only. |

## Agent workflows

Wait for an app to appear and settle before inspecting it:

1. Call `wait_for_window` with `processName`, a bounded timeout, and cadence.
2. Call `wait_for_stable` with the same selector, a stable duration, and a local `outputRoot` when evidence should be grouped.
3. Read `LastObservation.ScreenshotPath`, `TextPath`, and `MetadataPath` only if the task needs those local artifacts.

Wait for a status message to clear:

1. Call `wait_for_text` with the exact case-insensitive substring and a window selector.
2. Call `wait_for_disappear` with the same `textContains`. Because disappearance requires a prior positive observation within that call, it cannot report success for a message that was never seen.

Use `scripts\smoke-agent-watch.ps1` for a real Windows success-plus-timeout proof. The script launches one temporary test window, stops only that process, and writes its report under `artifacts\agent-watch-real`.
