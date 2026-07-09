const iconPaths = {
  menu: '<path d="M4 6h16M4 12h16M4 18h16"/>',
  capture: '<path d="M7 7h10l1.5 2H21v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V9h2.5L7 7Z"/><circle cx="12" cy="14" r="3"/>',
  codex: '<circle cx="12" cy="12" r="9"/><path d="m9 12 2 2 4-5"/>',
  context: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8M8 17h6"/>',
  image: '<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="8.5" cy="10.5" r="1.5"/><path d="m21 16-5-5L5 19"/>',
  sun: '<path d="M12 3v2M12 19v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M3 12h2M19 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4"/><circle cx="12" cy="12" r="4"/>',
  moon: '<path d="M20.6 15.6A8.5 8.5 0 0 1 8.4 3.4 8.5 8.5 0 1 0 20.6 15.6Z"/>',
  settings: '<path d="M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Z"/><path d="M19.4 15a1.8 1.8 0 0 0 .36 1.98l.05.05a2.1 2.1 0 0 1-2.97 2.97l-.05-.05a1.8 1.8 0 0 0-1.98-.36 1.8 1.8 0 0 0-1.08 1.65V21a2.1 2.1 0 0 1-4.2 0v-.08a1.8 1.8 0 0 0-1.08-1.65 1.8 1.8 0 0 0-1.98.36l-.05.05a2.1 2.1 0 0 1-2.97-2.97l.05-.05a1.8 1.8 0 0 0 .36-1.98 1.8 1.8 0 0 0-1.65-1.08H3a2.1 2.1 0 0 1 0-4.2h.08a1.8 1.8 0 0 0 1.65-1.08 1.8 1.8 0 0 0-.36-1.98l-.05-.05a2.1 2.1 0 0 1 2.97-2.97l.05.05a1.8 1.8 0 0 0 1.98.36h.02a1.8 1.8 0 0 0 1.06-1.65V3a2.1 2.1 0 0 1 4.2 0v.08a1.8 1.8 0 0 0 1.08 1.65 1.8 1.8 0 0 0 1.98-.36l.05-.05a2.1 2.1 0 0 1 2.97 2.97l-.05.05a1.8 1.8 0 0 0-.36 1.98v.02a1.8 1.8 0 0 0 1.65 1.06H21a2.1 2.1 0 0 1 0 4.2h-.08a1.8 1.8 0 0 0-1.52 1.08Z"/>',
  list: '<path d="M8 6h13M8 12h13M8 18h13"/><path d="M3 6h.01M3 12h.01M3 18h.01"/>',
  grid: '<rect x="4" y="4" width="6" height="6"/><rect x="14" y="4" width="6" height="6"/><rect x="4" y="14" width="6" height="6"/><rect x="14" y="14" width="6" height="6"/>',
  folder: '<path d="M3 6h6l2 2h10v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6Z"/>',
  copy: '<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
  trash: '<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M6 6l1 16h10l1-16"/>',
  timeline: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  session: '<rect x="3" y="6" width="14" height="12" rx="2"/><path d="m17 10 4-2v8l-4-2"/>',
  close: '<path d="M18 6 6 18M6 6l12 12"/>'
};

const state = {
  captures: [],
  filteredCaptures: [],
  selectedId: null,
  filter: "all",
  project: null,
  viewMode: "list",
  autoScroll: true,
  previewId: null,
  root: "",
  captureBusy: false,
  timelineRunning: false,
  sessionRunning: false,
  settingsOpen: false,
  packageInfo: null,
  startupSettings: null,
  startupBusy: false,
  backgroundSettings: null,
  backgroundBusy: false,
  packageInstallBusy: false,
  inspectorRequestId: 0,
  theme: "dark"
};

const elements = {
  shell: document.querySelector(".app-shell"),
  rows: document.querySelector("#capture-rows"),
  count: document.querySelector("#capture-count"),
  metrics: document.querySelector("#summary-metrics"),
  title: document.querySelector("#capture-set-title"),
  inspector: document.querySelector("#inspector-body"),
  projects: document.querySelector("#project-list"),
  recent: document.querySelector("#recent-list"),
  sync: document.querySelector("#sync-status"),
  tablePanel: document.querySelector(".table-panel"),
  autoScroll: document.querySelector("[data-auto-scroll]"),
  sidebarToggle: document.querySelector("[data-sidebar-toggle]"),
  themeToggle: document.querySelector("[data-theme-toggle]"),
  settingsModal: document.querySelector("[data-settings-modal]"),
  settingsMode: document.querySelector("[data-settings-mode]"),
  settingsPackage: document.querySelector("[data-settings-package]"),
  settingsLauncher: document.querySelector("[data-settings-launcher]"),
  settingsInstallCopy: document.querySelector("[data-settings-install-copy]"),
  settingsInstallButton: document.querySelector('[data-settings-action="install-local"]'),
  settingsStartup: document.querySelector("[data-settings-startup]"),
  settingsStartupCopy: document.querySelector("[data-settings-startup-copy]"),
  settingsBackground: document.querySelector("[data-settings-background]"),
  previewModal: document.querySelector("[data-preview-modal]"),
  previewTitle: document.querySelector("#preview-title"),
  previewMeta: document.querySelector("#preview-meta"),
  previewImage: document.querySelector("#preview-image"),
  captureCommand: document.querySelector('[data-command="capture"]'),
  captureCodexCommand: document.querySelector('[data-command="capture-codex"]'),
  timelineCommand: document.querySelector('[data-command="timeline"]'),
  sessionCommand: document.querySelector('[data-command="session"]'),
  intervalInput: document.querySelector("[data-interval-seconds]")
};

function svgIcon(name) {
  const path = iconPaths[name] || iconPaths.capture;
  return `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">${path}</svg>`;
}

function mountIcons(root = document) {
  root.querySelectorAll("[data-icon]").forEach((node) => {
    node.innerHTML = svgIcon(node.dataset.icon);
  });
}

function intervalMs() {
  const seconds = Math.min(3600, Math.max(5, Number(elements.intervalInput?.value || 60)));
  if (elements.intervalInput) {
    elements.intervalInput.value = String(seconds);
  }

  return seconds * 1000;
}

function setCaptureBusy(busy) {
  state.captureBusy = busy;
  elements.captureCommand.disabled = busy;
  elements.captureCodexCommand.disabled = busy;
  elements.intervalInput.disabled = busy || state.timelineRunning || state.sessionRunning;
  elements.timelineCommand.disabled = busy && !state.timelineRunning;
  elements.sessionCommand.disabled = busy && !state.sessionRunning;
}

function applyCaptureResponse(response) {
  state.root = response.root;
  state.captures = response.captures || [];
  refreshFilteredCaptures();
  state.selectedId = state.filteredCaptures[0]?.id || null;
  renderAll();
}

async function loadCaptures(statusText) {
  const response = await window.winshots.listCaptures();
  applyCaptureResponse(response);
  elements.sync.textContent = statusText || (state.captures.length > 0 ? "Local library ready" : "No local captures found");
}

function commandMessage(response, fallback) {
  const command = response?.command;
  if (command?.CodexPasteMessage || command?.codexPasteMessage) {
    return command.CodexPasteMessage || command.codexPasteMessage;
  }

  if (command?.DirectoryPath || command?.directoryPath) {
    return fallback;
  }

  return command?.Message || command?.message || fallback;
}

async function runAppCapture({ pasteToCodex = false, reason = "electron", status = "Capturing..." } = {}) {
  if (state.captureBusy) {
    return;
  }

  try {
    setCaptureBusy(true);
    elements.sync.textContent = status;
    const response = await window.winshots.captureNow({
      pasteToCodex,
      reason
    });
    applyCaptureResponse(response);
    elements.sync.textContent = commandMessage(response, pasteToCodex ? "Captured for Codex" : "Capture saved");
  } catch (error) {
    elements.sync.textContent = error.message || "Capture failed";
  } finally {
    setCaptureBusy(false);
  }
}

async function toggleTimeline() {
  try {
    elements.timelineCommand.disabled = true;
    elements.sync.textContent = state.timelineRunning ? "Stopping timeline..." : "Starting timeline...";
    const response = await window.winshots.toggleTimeline({
      intervalMs: intervalMs()
    });
    state.timelineRunning = Boolean(response?.Running ?? response?.running);
    elements.timelineCommand.classList.toggle("active", state.timelineRunning);
    elements.timelineCommand.setAttribute("aria-pressed", String(state.timelineRunning));
    elements.timelineCommand.querySelector("span:last-child").textContent = state.timelineRunning ? "Stop timeline" : "Timeline";
    elements.sync.textContent = response?.Message || response?.message || (state.timelineRunning ? "Timeline running" : "Timeline stopped");
    await loadCaptures(elements.sync.textContent);
  } catch (error) {
    elements.sync.textContent = error.message || "Timeline failed";
  } finally {
    elements.timelineCommand.disabled = false;
    setCaptureBusy(state.captureBusy);
  }
}

async function toggleSession() {
  if (state.sessionRunning) {
    try {
      elements.sessionCommand.disabled = true;
      elements.sync.textContent = "Finalizing session...";
      const response = await window.winshots.stopVisualSession();
      state.sessionRunning = false;
      elements.sessionCommand.classList.remove("active");
      elements.sessionCommand.setAttribute("aria-pressed", "false");
      elements.sessionCommand.querySelector("span:last-child").textContent = "Session";
      const frames = response?.manifest?.CapturedFrameCount ?? 0;
      elements.sync.textContent = `Session saved (${frames} frames)`;
    } catch (error) {
      elements.sync.textContent = error.message || "Session stop failed";
    } finally {
      elements.sessionCommand.disabled = false;
      setCaptureBusy(state.captureBusy);
    }
    return;
  }

  try {
    elements.sessionCommand.disabled = true;
    elements.sync.textContent = "Starting session...";
    await window.winshots.startVisualSession({
      intervalMs: intervalMs(),
      durationSeconds: 300
    });
    state.sessionRunning = true;
    elements.sessionCommand.classList.add("active");
    elements.sessionCommand.setAttribute("aria-pressed", "true");
    elements.sessionCommand.querySelector("span:last-child").textContent = "Stop session";
    elements.sync.textContent = "Session recording";
  } catch (error) {
    elements.sync.textContent = error.message || "Session start failed";
  } finally {
    elements.sessionCommand.disabled = false;
    setCaptureBusy(state.captureBusy);
  }
}

function formatTime(capture) {
  const source = capture.timestampLocal || capture.timestampUtc;
  const match = String(source).match(/\b(\d{2}):(\d{2}):(\d{2})\b/);
  return match ? `${match[1]}:${match[2]}:${match[3]}` : "--:--:--";
}

function formatAge(capture) {
  const date = new Date(capture.timestampUtc);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  const diffMs = Date.now() - date.getTime();
  const minutes = Math.max(1, Math.round(diffMs / 60_000));
  if (minutes < 60) {
    return `${minutes}m`;
  }

  const hours = Math.round(minutes / 60);
  if (hours < 48) {
    return `${hours}h`;
  }

  return `${Math.round(hours / 24)}d`;
}

function formatDuration(capture) {
  const total = Number(capture.metrics?.TotalMs || capture.metrics?.totalMs || 0);
  if (!total) {
    return "-";
  }

  return total >= 1000 ? `${(total / 1000).toFixed(1)}s` : `${total}ms`;
}

function formatBytes(bytes) {
  const value = Number(bytes || 0);
  if (!value) {
    return "-";
  }

  if (value > 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)} MB`;
  }

  return `${Math.round(value / 1000)} KB`;
}

function labelsFor(capture) {
  const text = `${capture.windowTitle} ${capture.processName} ${capture.reason}`.toLowerCase();
  const labels = [];
  if (text.includes("codex")) labels.push("codex");
  if (text.includes("brave")) labels.push("web");
  if (text.includes("twitch")) labels.push("stream");
  if (text.includes("hotkey")) labels.push("hotkey");
  if (text.includes("periodic")) labels.push("periodic");
  if (text.includes("mcp")) labels.push("mcp");
  if (labels.length < 2) labels.push(capture.processName.toLowerCase());
  return [...new Set(labels)].slice(0, 3);
}

function captureTitle(capture) {
  return capture.windowTitle || capture.id || "Untitled capture";
}

function projectName(capture) {
  const title = capture.windowTitle || "";
  if (title.includes("Codex")) return "Codex";
  if (title.includes("Twitch")) return "Twitch";
  if (title.includes("Google")) return "Browser";
  return capture.processName || "Local";
}

function matchesPrimaryFilter(capture) {
  if (state.filter === "codex") {
    const text = `${capture.reason} ${capture.windowTitle} ${capture.processName}`.toLowerCase();
    return text.includes("codex");
  }

  if (state.filter === "context") {
    return capture.extractedTextLength > 0;
  }

  if (state.filter === "images") {
    return Boolean(capture.screenshotUrl);
  }

  return true;
}

function applyFilters(captures = state.captures) {
  return captures.filter((capture) => {
    const projectMatches = !state.project || projectName(capture) === state.project;
    return projectMatches && matchesPrimaryFilter(capture);
  });
}

function refreshFilteredCaptures() {
  state.filteredCaptures = applyFilters();
  if (!state.filteredCaptures.some((capture) => capture.id === state.selectedId)) {
    state.selectedId = state.filteredCaptures[0]?.id || null;
  }
}

function renderMetrics() {
  const captureCount = state.filteredCaptures.length;
  const latest = state.filteredCaptures[0];
  const resolution = latest?.resolution || "No captures";

  const metrics = [
    { icon: "capture", label: `${captureCount} captures` },
    { icon: "timeline", label: latest ? `${formatAge(latest)} ago` : "No recent captures" },
    { icon: "image", label: resolution }
  ];

  elements.metrics.innerHTML = metrics
    .map((metric) => `<span class="metric">${svgIcon(metric.icon)}${escapeHtml(metric.label)}</span>`)
    .join("");
}

function renderRows() {
  elements.tablePanel.classList.toggle("grid-mode", state.viewMode === "grid");

  if (state.filteredCaptures.length === 0) {
    elements.rows.innerHTML = `
      <div class="empty-state">
        <strong>No captures found</strong>
        <span>Winshots looked in ${escapeHtml(state.root || "the default capture root")}.</span>
      </div>
    `;
    elements.count.textContent = "0 captures";
    return;
  }

  if (state.viewMode === "grid") {
    renderGridRows();
  } else {
    renderListRows();
  }

  elements.count.textContent = `${state.filteredCaptures.length} captures`;
  scrollSelectionIntoView();
}

function renderListRows() {
  elements.rows.innerHTML = state.filteredCaptures
    .map((capture, index) => {
      const active = capture.id === state.selectedId ? " active" : "";
      const labels = labelsFor(capture)
        .map((label) => `<span class="label-pill"><span></span>${escapeHtml(label)}</span>`)
        .join("");

      return `
        <div class="capture-row data-row${active}" role="row" aria-selected="${capture.id === state.selectedId}" tabindex="0" data-capture-row data-capture-id="${escapeHtml(capture.id)}">
          <span role="cell">${String(index + 1).padStart(2, "0")}</span>
          <span role="cell" class="preview-cell">${thumbnailMarkup(capture)}</span>
          <span role="cell" class="title-cell">${escapeHtml(captureTitle(capture))}</span>
          <span role="cell">${formatTime(capture)}</span>
          <span role="cell">${formatDuration(capture)}</span>
          <span role="cell" class="labels-cell">${labels}</span>
        </div>
      `;
    })
    .join("");
}

function renderGridRows() {
  elements.rows.innerHTML = state.filteredCaptures
    .map((capture) => {
      const active = capture.id === state.selectedId ? " active" : "";
      const labels = labelsFor(capture)
        .map((label) => `<span class="label-pill"><span></span>${escapeHtml(label)}</span>`)
        .join("");

      return `
        <article class="capture-card${active}" aria-current="${capture.id === state.selectedId}" tabindex="0" data-capture-row data-capture-id="${escapeHtml(capture.id)}">
          ${thumbnailMarkup(capture)}
          <div>
            <span class="capture-card-title">${escapeHtml(captureTitle(capture))}</span>
            <div class="capture-card-meta">
              <span>${formatTime(capture)}</span>
              <span>${formatDuration(capture)}</span>
            </div>
            <div class="labels-cell">${labels}</div>
          </div>
        </article>
      `;
    })
    .join("");
}

function thumbnailMarkup(capture) {
  if (!capture.screenshotUrl) {
    return `<span class="missing-thumb">No image</span>`;
  }

  return `
    <button type="button" class="thumbnail-button" data-open-preview data-capture-id="${escapeHtml(capture.id)}" aria-label="Open large preview for ${escapeHtml(captureTitle(capture))}">
      <img src="${capture.screenshotUrl}" alt="Screenshot preview for ${escapeHtml(captureTitle(capture))}" />
    </button>
  `;
}

function renderProjects() {
  const counts = new Map();
  state.captures.forEach((capture) => {
    const name = projectName(capture);
    counts.set(name, (counts.get(name) || 0) + 1);
  });

  if (counts.size === 0) {
    elements.projects.innerHTML = `<p class="muted">No projects</p>`;
    return;
  }

  elements.projects.innerHTML = [...counts.entries()]
    .slice(0, 5)
    .map(([name, count]) => {
      const active = name === state.project ? " active" : "";
      return `
        <button type="button" class="project-item${active}" data-project="${escapeHtml(name)}" aria-pressed="${name === state.project}">
          <span>${svgIcon("folder")}${escapeHtml(name)}</span>
          <span>${count}</span>
        </button>
      `;
    })
    .join("");
}

function renderRecent() {
  elements.recent.innerHTML = state.captures
    .slice(0, 5)
    .map((capture) => {
      const active = capture.id === state.selectedId ? " active" : "";
      return `
        <button type="button" class="recent-item${active}" data-capture-id="${escapeHtml(capture.id)}">
          <span>${svgIcon("capture")}${escapeHtml(capture.id)}</span>
          <span>${formatAge(capture)}</span>
        </button>
      `;
    })
    .join("");
}

async function renderInspector() {
  const requestId = ++state.inspectorRequestId;
  const capture = state.captures.find((item) => item.id === state.selectedId);
  if (!capture) {
    elements.inspector.innerHTML = `<p class="muted">Select a capture to inspect local metadata.</p>`;
    return;
  }

  let context = capture.contextPreview || "";
  if (!capture.detailsLoaded) {
    try {
      const response = await window.winshots.readCaptureContext(capture.id);
      if (requestId !== state.inspectorRequestId || state.selectedId !== capture.id) {
        return;
      }

      capture.contextPreview = response.context || context;
      capture.hash = response.hash || "";
      capture.detailsLoaded = true;
      context = capture.contextPreview;
    } catch {
      if (requestId !== state.inspectorRequestId || state.selectedId !== capture.id) {
        return;
      }

      context = context || "Context text is not available for this capture.";
    }
  }

  if (requestId !== state.inspectorRequestId || state.selectedId !== capture.id) {
    return;
  }

  const labels = labelsFor(capture)
    .map((label) => `<span class="tag">${escapeHtml(label)}</span>`)
    .join("");

  elements.inspector.innerHTML = `
    <section class="inspector-section">
      <h3>Capture ${String(state.filteredCaptures.findIndex((item) => item.id === capture.id) + 1).padStart(2, "0")}</h3>
      <h4>Information</h4>
      ${infoRow("Title", captureTitle(capture))}
      ${infoRow("Captured", capture.timestampLocal || capture.timestampUtc)}
      ${infoRow("Reason", capture.reason)}
      ${infoRow("Duration", formatDuration(capture))}
      ${infoRow("Resolution", capture.resolution)}
      ${infoRow("Window", capture.processName)}
      ${infoRow("Source", capture.windowHandle || "Display 1")}
      ${infoRow("File", "screenshot.png")}
      ${infoRow("Size", formatBytes(capture.screenshotBytes))}
      ${infoRow("Text", `${Number(capture.extractedTextLength || 0).toLocaleString()} chars`)}
      ${infoRow("Hash", capture.hash ? `${capture.hash.slice(0, 12)}...` : "-")}
    </section>
    <section class="inspector-section">
      <h4>Labels</h4>
      <div class="tag-row">${labels}</div>
    </section>
    <section class="inspector-section">
      <h4>Context</h4>
      <textarea class="context-box" rows="8" spellcheck="false" readonly>${escapeHtml(contextPreview(context))}</textarea>
    </section>
    <section class="inspector-section">
      <h4>Actions</h4>
      ${actionButton("open-preview", "image", "Open large preview", !capture.screenshotUrl)}
      ${actionButton("open-folder", "folder", "Open file location")}
      ${actionButton("copy-image", "image", "Copy image", !capture.screenshotUrl)}
      ${actionButton("copy-path", "copy", "Copy image path", !capture.screenshotPath)}
      ${actionButton("copy-context", "context", "Copy context", !capture.textPath)}
      ${actionButton("copy-metadata", "copy", "Copy metadata", !capture.metadataPath)}
      ${actionButton("trash", "trash", "Move to Recycle Bin", false, true)}
    </section>
  `;
}

function actionButton(action, icon, label, disabled = false, danger = false) {
  const disabledAttributes = disabled ? ' disabled aria-disabled="true"' : "";
  const dangerClass = danger ? " danger" : "";
  return `<button type="button" class="action-row${dangerClass}" data-action="${action}"${disabledAttributes}>${svgIcon(icon)}${escapeHtml(label)}</button>`;
}

function contextPreview(context) {
  const clean = String(context || "").replace(/\uFFFC/g, "").trim();
  return clean.length > 2400 ? `${clean.slice(0, 2400).trimEnd()}...` : clean;
}

function infoRow(label, value) {
  return `<div class="info-row"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value || "-")}</strong></div>`;
}

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

function updateSelection(captureId) {
  state.selectedId = captureId;
  elements.rows.querySelectorAll("[data-capture-row]").forEach((row) => {
    const active = row.dataset.captureId === captureId;
    row.classList.toggle("active", active);
    if (row.getAttribute("role") === "row") {
      row.setAttribute("aria-selected", String(active));
    } else {
      row.setAttribute("aria-current", String(active));
    }
  });
  renderRecent();
  renderInspector();
}

function applyActiveFilter(filter) {
  state.filter = filter;
  state.project = null;
  refreshFilteredCaptures();
  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.toggle("active", item.dataset.filter === filter);
  });
  updateTitle();
  renderAll();
}

function applyProjectFilter(project) {
  state.project = project;
  state.filter = "all";
  refreshFilteredCaptures();
  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.toggle("active", item.dataset.filter === "all");
  });
  updateTitle();
  renderAll();
}

function titleForFilter(filter) {
  const titles = {
    all: "Captures",
    codex: "Codex captures",
    context: "Captures with context",
    images: "Captures with screenshots"
  };

  return titles[filter] || "Captures";
}

function updateTitle() {
  elements.title.textContent = state.project ? `${state.project} captures` : titleForFilter(state.filter);
}

function renderAll() {
  updateTitle();
  renderMetrics();
  renderRows();
  renderProjects();
  renderRecent();
  renderInspector();
  renderPreview();
}

function setViewMode(mode) {
  state.viewMode = mode === "grid" ? "grid" : "list";
  document.querySelectorAll("[data-view-mode]").forEach((button) => {
    const active = button.dataset.viewMode === state.viewMode;
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", String(active));
  });
  elements.sync.textContent = state.viewMode === "grid" ? "Grid view" : "List view";
  renderRows();
}

function modeLabel(mode) {
  if (mode === "installed") return "Installed";
  if (mode === "portable") return "Portable package";
  return "Source checkout";
}

function installCopy(info) {
  if (!info) return "Package status is loading.";
  if (info.mode === "installed") {
    return "This copy is already installed under the local user profile.";
  }
  if (info.canInstall) {
    return info.installTargetReady
      ? "Install locally refreshes the copy under the user profile and Start Menu shortcuts. Codex plugin setup stays separate."
      : "Install locally copies this package to the user profile and creates Start Menu shortcuts. Codex plugin setup stays separate.";
  }

  return "Build or extract the release zip to enable local install.";
}

function launcherCopy(info) {
  if (!info) return "Package status is loading.";
  const starts = (info.starts || []).join(", ");
  return `The launcher starts ${starts}. Use the release setup executable for installation, or run this portable package directly from its folder.`;
}

function renderSettings() {
  const info = state.packageInfo;
  const mode = modeLabel(info?.mode);
  elements.settingsMode.textContent = info ? mode : "Checking package...";
  elements.settingsPackage.innerHTML = info
    ? [
        infoRow("Mode", mode),
        infoRow("Package", info.packageRoot),
        infoRow("Install target", info.installRoot),
        infoRow("Installed copy", info.installTargetReady ? "Present" : "Missing"),
        infoRow("Electron runtime", info.hasElectronRuntime ? "Available" : "Missing"),
        infoRow("MCP server", info.hasMcpServer ? "Available" : "Missing")
      ].join("")
    : `<p class="muted">Checking package...</p>`;
  elements.settingsLauncher.textContent = launcherCopy(info);
  elements.settingsInstallCopy.textContent = installCopy(info);
  elements.settingsInstallButton.disabled = state.packageInstallBusy || !info?.canInstall;
  elements.settingsInstallButton.textContent = state.packageInstallBusy ? "Installing..." : "Install locally";
  elements.settingsStartup.checked = Boolean(state.startupSettings?.enabled);
  elements.settingsStartup.disabled = state.startupBusy || !state.startupSettings?.available;
  elements.settingsStartupCopy.textContent = state.startupSettings?.available
    ? "Starts the Winshots host and review UI when you sign in to Windows."
    : "Install Winshots locally to make this option available.";
  elements.settingsBackground.checked = Boolean(state.backgroundSettings?.enabled);
  elements.settingsBackground.disabled = state.backgroundBusy;
}

async function loadPackageInfo() {
  try {
    [state.packageInfo, state.startupSettings, state.backgroundSettings] = await Promise.all([
      window.winshots.packageInfo(),
      window.winshots.getStartupSettings(),
      window.winshots.getBackgroundSettings()
    ]);
  } catch (error) {
    state.packageInfo = null;
    elements.sync.textContent = error.message || "Package check failed";
  }

  renderSettings();
}

function openSettings() {
  state.settingsOpen = true;
  elements.settingsModal.hidden = false;
  renderSettings();
  loadPackageInfo();
  elements.settingsModal.querySelector("[data-settings-close]")?.focus();
}

function closeSettings() {
  state.settingsOpen = false;
  elements.settingsModal.hidden = true;
}

async function runSettingsAction(action) {
  if (action === "open-package") {
    await window.winshots.openPackageFolder();
    return "Opened package folder";
  }

  if (action === "install-local") {
    if (!window.confirm("Install Winshots to the local user profile? Codex plugin setup stays separate so a locked Codex cache cannot block the app install.")) {
      return "";
    }

    state.packageInstallBusy = true;
    renderSettings();
    const response = await window.winshots.installPackage();
    state.packageInfo = response.info || state.packageInfo;
    state.packageInstallBusy = false;
    renderSettings();
    return "Winshots installed locally";
  }

  return "";
}

function scrollSelectionIntoView() {
  if (!state.autoScroll || !state.selectedId) {
    return;
  }

  requestAnimationFrame(() => {
    const selectedRow = [...elements.rows.querySelectorAll("[data-capture-row]")]
      .find((row) => row.dataset.captureId === state.selectedId);
    selectedRow?.scrollIntoView({ block: "nearest" });
  });
}

function toggleSidebar() {
  const collapsed = !elements.shell.classList.contains("sidebar-collapsed");
  elements.shell.classList.toggle("sidebar-collapsed", collapsed);
  elements.sidebarToggle.setAttribute("aria-pressed", String(collapsed));
  elements.sync.textContent = collapsed ? "Sidebar collapsed" : "Sidebar expanded";
}

function systemTheme() {
  return window.matchMedia?.("(prefers-color-scheme: light)")?.matches ? "light" : "dark";
}

function storedTheme() {
  try {
    const theme = window.localStorage.getItem("winshots.theme");
    return theme === "light" || theme === "dark" ? theme : null;
  } catch {
    return null;
  }
}

function applyTheme(theme, announce = false) {
  const nextTheme = theme === "light" ? "light" : "dark";
  const targetTheme = nextTheme === "dark" ? "light" : "dark";
  const targetLabel = `Switch to ${targetTheme} theme`;

  state.theme = nextTheme;
  document.documentElement.dataset.theme = nextTheme;
  elements.themeToggle.dataset.icon = targetTheme === "light" ? "sun" : "moon";
  elements.themeToggle.innerHTML = svgIcon(elements.themeToggle.dataset.icon);
  elements.themeToggle.setAttribute("aria-label", targetLabel);
  elements.themeToggle.setAttribute("title", targetLabel);
  elements.themeToggle.setAttribute("aria-pressed", String(nextTheme === "light"));

  if (announce) {
    elements.sync.textContent = `${nextTheme === "light" ? "Light" : "Dark"} theme enabled`;
  }
}

function applyStoredTheme() {
  applyTheme(storedTheme() || systemTheme());
}

function toggleTheme() {
  const nextTheme = state.theme === "dark" ? "light" : "dark";
  applyTheme(nextTheme, true);
  try {
    window.localStorage.setItem("winshots.theme", nextTheme);
  } catch {
    // Local storage is best-effort for file-backed Electron pages.
  }
}

function captureById(captureId) {
  return state.captures.find((capture) => capture.id === captureId);
}

function openPreview(captureId) {
  const capture = captureById(captureId);
  if (!capture?.screenshotUrl) {
    elements.sync.textContent = "No screenshot is available for this capture";
    return;
  }

  if (state.selectedId !== capture.id) {
    updateSelection(capture.id);
  }

  state.previewId = capture.id;
  elements.previewModal.hidden = false;
  renderPreview();
  elements.previewModal.querySelector("[data-preview-close]")?.focus();
}

function closePreview() {
  state.previewId = null;
  elements.previewModal.hidden = true;
  elements.previewImage.removeAttribute("src");
}

function renderPreview() {
  if (!state.previewId || elements.previewModal.hidden) {
    return;
  }

  const capture = captureById(state.previewId);
  if (!capture?.screenshotUrl) {
    closePreview();
    return;
  }

  elements.previewTitle.textContent = captureTitle(capture);
  elements.previewMeta.textContent = `${capture.id} | ${capture.timestampLocal || capture.timestampUtc || "Unknown time"} | ${capture.resolution}`;
  elements.previewImage.src = capture.screenshotUrl;
  elements.previewImage.alt = `Large screenshot preview for ${captureTitle(capture)}`;
  setModalAction("copy-image", "image", "Copy image", !capture.screenshotUrl);
  setModalAction("copy-path", "copy", "Copy path", !capture.screenshotPath);
  setModalAction("open-folder", "folder", "Open folder", false);
}

function setModalAction(action, icon, label, disabled = false) {
  const button = elements.previewModal.querySelector(`[data-modal-action="${action}"]`);
  if (!button) {
    return;
  }

  button.innerHTML = `${svgIcon(icon)}${escapeHtml(label)}`;
  button.disabled = disabled;
  button.setAttribute("aria-disabled", String(disabled));
}

async function runCaptureAction(action, captureId) {
  if (action === "open-preview") {
    openPreview(captureId);
    return "";
  }

  if (action === "open-folder") {
    await window.winshots.openCaptureFolder(captureId);
    return "Opened capture folder";
  }

  if (action === "copy-image") {
    await window.winshots.copyScreenshotImage(captureId);
    return "Image copied";
  }

  if (action === "copy-path") {
    await window.winshots.copyScreenshotPath(captureId);
    return "Image path copied";
  }

  if (action === "copy-context") {
    await window.winshots.copyCaptureContext(captureId);
    return "Context copied";
  }

  if (action === "copy-metadata") {
    await window.winshots.copyCaptureMetadata(captureId);
    return "Metadata copied";
  }

  if (action === "trash") {
    if (!window.confirm(`Move capture ${captureId} to the Recycle Bin?`)) {
      return "";
    }

    const response = await window.winshots.trashCapture(captureId);
    state.root = response.root;
    state.captures = response.captures || [];
    refreshFilteredCaptures();
    if (state.previewId === captureId) {
      closePreview();
    }
    renderAll();
    return "Capture moved to Recycle Bin";
  }

  return "";
}

function bindEvents() {
  document.addEventListener("click", async (event) => {
    const disabledControl = event.target.closest("button[disabled], [aria-disabled='true']");
    if (disabledControl) {
      event.preventDefault();
      return;
    }

    if (event.target.closest("[data-preview-close]")) {
      closePreview();
      return;
    }

    if (event.target.closest("[data-settings-close]")) {
      closeSettings();
      return;
    }

    const settingsAction = event.target.closest("[data-settings-action]");
    if (settingsAction) {
      try {
        const status = await runSettingsAction(settingsAction.dataset.settingsAction);
        if (status) elements.sync.textContent = status;
      } catch (error) {
        state.packageInstallBusy = false;
        renderSettings();
        elements.sync.textContent = error.message || "Settings action failed";
      }
      return;
    }

    const modalAction = event.target.closest("[data-modal-action]");
    if (modalAction && state.previewId) {
      try {
        const status = await runCaptureAction(modalAction.dataset.modalAction, state.previewId);
        if (status) elements.sync.textContent = status;
      } catch (error) {
        elements.sync.textContent = error.message || "Capture action failed";
      }
      return;
    }

    const previewButton = event.target.closest("[data-open-preview]");
    if (previewButton) {
      openPreview(previewButton.dataset.captureId);
      return;
    }

    const commandButton = event.target.closest("[data-command]");
    if (commandButton) {
      const command = commandButton.dataset.command;
      if (command === "capture") {
        await runAppCapture({ reason: "electron", status: "Capturing..." });
      }
      if (command === "capture-codex") {
        await runAppCapture({ pasteToCodex: true, reason: "electron-codex", status: "Capturing for Codex..." });
      }
      if (command === "timeline") {
        await toggleTimeline();
      }
      if (command === "session") {
        await toggleSession();
      }
      return;
    }

    const viewButton = event.target.closest("[data-view-mode]");
    if (viewButton) {
      setViewMode(viewButton.dataset.viewMode);
      return;
    }

    if (event.target.closest("[data-sidebar-toggle]")) {
      toggleSidebar();
      return;
    }

    if (event.target.closest("[data-theme-toggle]")) {
      toggleTheme();
      return;
    }

    if (event.target.closest("[data-settings-open]")) {
      openSettings();
      return;
    }

    const projectButton = event.target.closest("[data-project]");
    if (projectButton) {
      applyProjectFilter(projectButton.dataset.project);
      return;
    }

    const filterButton = event.target.closest(".nav-item[data-filter]");
    if (filterButton) {
      applyActiveFilter(filterButton.dataset.filter);
      return;
    }

    const windowButton = event.target.closest("[data-window-action]");
    if (windowButton) {
      const action = windowButton.dataset.windowAction;
      if (action === "minimize") await window.winshots.minimize();
      if (action === "maximize") await window.winshots.maximize();
      if (action === "close") await window.winshots.close();
      return;
    }

    const inspectorAction = event.target.closest("[data-action]");
    if (inspectorAction && state.selectedId) {
      try {
        const status = await runCaptureAction(inspectorAction.dataset.action, state.selectedId);
        if (status) elements.sync.textContent = status;
      } catch (error) {
        elements.sync.textContent = error.message || "Capture action failed";
      }
      return;
    }

    const recentButton = event.target.closest(".recent-item[data-capture-id]");
    if (recentButton) {
      updateSelection(recentButton.dataset.captureId);
      return;
    }

    const captureRow = event.target.closest("[data-capture-row]");
    if (captureRow) {
      updateSelection(captureRow.dataset.captureId);
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !elements.settingsModal.hidden) {
      closeSettings();
      return;
    }

    if (event.key === "Escape" && !elements.previewModal.hidden) {
      closePreview();
      return;
    }

    const captureRow = event.target.closest("[data-capture-row]");
    if (captureRow && event.target === captureRow && (event.key === "Enter" || event.key === " ")) {
      event.preventDefault();
      updateSelection(captureRow.dataset.captureId);
    }
  });

  elements.autoScroll.addEventListener("change", () => {
    state.autoScroll = elements.autoScroll.checked;
    elements.sync.textContent = state.autoScroll ? "Auto-scroll enabled" : "Auto-scroll disabled";
    scrollSelectionIntoView();
  });

  elements.settingsStartup.addEventListener("change", async () => {
    const enabled = elements.settingsStartup.checked;
    state.startupBusy = true;
    renderSettings();
    try {
      state.startupSettings = await window.winshots.setStartupEnabled(enabled);
      elements.sync.textContent = enabled
        ? "Winshots will launch at Windows startup"
        : "Launch at startup disabled";
    } catch (error) {
      elements.sync.textContent = error.message || "Startup setting failed";
    } finally {
      state.startupBusy = false;
      renderSettings();
    }
  });

  elements.settingsBackground.addEventListener("change", async () => {
    const enabled = elements.settingsBackground.checked;
    state.backgroundBusy = true;
    renderSettings();
    try {
      state.backgroundSettings = await window.winshots.setBackgroundEnabled(enabled);
      elements.sync.textContent = enabled
        ? "Winshots will keep running in the background"
        : "Background mode disabled";
    } catch (error) {
      elements.sync.textContent = error.message || "Background setting failed";
    } finally {
      state.backgroundBusy = false;
      renderSettings();
    }
  });
}

async function waitForImages() {
  const images = [...document.images];
  const loads = Promise.all(
    images.map((image) => {
      if (image.complete) return Promise.resolve();
      return new Promise((resolve) => {
        image.addEventListener("load", resolve, { once: true });
        image.addEventListener("error", resolve, { once: true });
      });
    })
  );

  await Promise.race([loads, new Promise((resolve) => setTimeout(resolve, 3500))]);
}

async function init() {
  mountIcons();
  applyStoredTheme();
  requestAnimationFrame(() => document.body.classList.add("theme-transitions"));
  bindEvents();
  window.winshots.onCapturesChanged(async () => {
    try {
      await loadCaptures("Synced just now");
    } catch (error) {
      elements.sync.textContent = error.message || "Capture sync failed";
    }
  });

  try {
    await loadCaptures();
  } catch (error) {
    elements.sync.textContent = "Capture load failed";
    elements.rows.innerHTML = `<div class="empty-state"><strong>Capture load failed</strong><span>${escapeHtml(error.message)}</span></div>`;
  }

  await waitForImages();
  requestAnimationFrame(() => window.winshots.notifyReady());
}

init();
