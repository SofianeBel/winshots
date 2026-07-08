const iconPaths = {
  menu: '<path d="M4 6h16M4 12h16M4 18h16"/>',
  capture: '<path d="M7 7h10l1.5 2H21v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V9h2.5L7 7Z"/><circle cx="12" cy="14" r="3"/>',
  codex: '<circle cx="12" cy="12" r="9"/><path d="m9 12 2 2 4-5"/>',
  context: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8M8 17h6"/>',
  image: '<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="8.5" cy="10.5" r="1.5"/><path d="m21 16-5-5L5 19"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M4.9 4.9 7 7M17 17l2.1 2.1M2 12h3M19 12h3M4.9 19.1 7 17M17 7l2.1-2.1"/>',
  theme: '<path d="M12 3v2M12 19v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M3 12h2M19 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4"/><circle cx="12" cy="12" r="4"/>',
  edit: '<path d="m4 16-.8 4 4-.8L18.5 7.9a2.1 2.1 0 0 0-3-3L4 16Z"/><path d="m13.5 6.5 4 4"/>',
  list: '<path d="M8 6h13M8 12h13M8 18h13"/><path d="M3 6h.01M3 12h.01M3 18h.01"/>',
  grid: '<rect x="4" y="4" width="6" height="6"/><rect x="14" y="4" width="6" height="6"/><rect x="4" y="14" width="6" height="6"/><rect x="14" y="14" width="6" height="6"/>',
  tune: '<path d="M4 7h10M18 7h2M4 17h2M10 17h10"/><circle cx="16" cy="7" r="2"/><circle cx="8" cy="17" r="2"/>',
  folder: '<path d="M3 6h6l2 2h10v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6Z"/>',
  copy: '<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
  trash: '<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M6 6l1 16h10l1-16"/>'
};

const state = {
  captures: [],
  filteredCaptures: [],
  selectedId: null,
  filter: "all",
  root: "",
  source: ""
};

const elements = {
  rows: document.querySelector("#capture-rows"),
  count: document.querySelector("#capture-count"),
  metrics: document.querySelector("#summary-metrics"),
  title: document.querySelector("#capture-set-title"),
  inspector: document.querySelector("#inspector-body"),
  projects: document.querySelector("#project-list"),
  recent: document.querySelector("#recent-list"),
  sync: document.querySelector("#sync-status")
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

function applyFilter(captures) {
  if (state.filter === "codex") {
    return captures.filter((capture) => capture.reason.includes("codex"));
  }

  if (state.filter === "context") {
    return captures.filter((capture) => capture.extractedTextLength > 0);
  }

  if (state.filter === "images") {
    return captures.filter((capture) => Boolean(capture.screenshotUrl));
  }

  return captures;
}

function renderMetrics() {
  const captureCount = state.filteredCaptures.length;
  const latest = state.filteredCaptures[0];
  const resolution = latest?.resolution || "No captures";
  const source = state.source || "local files";
  const textNodes = state.filteredCaptures.reduce((sum, capture) => {
    return sum + Number(capture.metrics?.AutomationNodeCount || capture.metrics?.automationNodeCount || 0);
  }, 0);

  const metrics = [
    { icon: "capture", label: `${captureCount} captures` },
    { icon: "codex", label: latest ? formatAge(latest) : "empty" },
    { icon: "image", label: resolution },
    { icon: "list", label: source },
    { icon: "context", label: `${textNodes.toLocaleString()} nodes` }
  ];

  elements.metrics.innerHTML = metrics
    .map((metric) => `<span class="metric">${svgIcon(metric.icon)}${escapeHtml(metric.label)}</span>`)
    .join("");
}

function renderRows() {
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

  elements.rows.innerHTML = state.filteredCaptures
    .map((capture, index) => {
      const active = capture.id === state.selectedId ? " active" : "";
      const thumbnail = capture.screenshotUrl
        ? `<img src="${capture.screenshotUrl}" alt="Screenshot preview for ${escapeHtml(captureTitle(capture))}" />`
        : `<span class="missing-thumb">No image</span>`;
      const labels = labelsFor(capture)
        .map((label) => `<span class="label-pill"><span></span>${escapeHtml(label)}</span>`)
        .join("");

      return `
        <button class="capture-row data-row${active}" role="row" type="button" data-capture-id="${escapeHtml(capture.id)}">
          <span role="cell">${String(index + 1).padStart(2, "0")}</span>
          <span role="cell" class="preview-cell">${thumbnail}</span>
          <span role="cell" class="title-cell">${escapeHtml(captureTitle(capture))}</span>
          <span role="cell">${formatTime(capture)}</span>
          <span role="cell">${formatDuration(capture)}</span>
          <span role="cell" class="labels-cell">${labels}</span>
          <span role="cell" class="more-cell">...</span>
        </button>
      `;
    })
    .join("");

  elements.count.textContent = `${state.filteredCaptures.length} captures`;
}

function renderProjects() {
  const counts = new Map();
  state.captures.forEach((capture) => {
    const name = projectName(capture);
    counts.set(name, (counts.get(name) || 0) + 1);
  });

  elements.projects.innerHTML = [...counts.entries()]
    .slice(0, 5)
    .map(([name, count]) => {
      return `
        <button type="button" class="project-item">
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
  const capture = state.captures.find((item) => item.id === state.selectedId);
  if (!capture) {
    elements.inspector.innerHTML = `<p class="muted">Select a capture to inspect local metadata.</p>`;
    return;
  }

  let context = capture.contextPreview || "";
  try {
    const response = await window.winshots.readCaptureContext(capture.id);
    context = response.context || context;
  } catch {
    context = context || "Context text is not available for this capture.";
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
      <button type="button" class="action-row" data-action="open-folder">${svgIcon("folder")}Open file location</button>
      <button type="button" class="action-row" data-action="copy-image">${svgIcon("image")}Copy image</button>
      <button type="button" class="action-row" data-action="copy-path">${svgIcon("copy")}Copy image path</button>
      <button type="button" class="action-row" data-action="copy-context">${svgIcon("context")}Copy context</button>
      <button type="button" class="action-row" data-action="copy-metadata">${svgIcon("copy")}Copy metadata</button>
      <button type="button" class="action-row danger" data-action="trash">${svgIcon("trash")}Move to Recycle Bin</button>
    </section>
  `;
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
  renderRows();
  renderRecent();
  renderInspector();
}

function applyActiveFilter(filter) {
  state.filter = filter;
  state.filteredCaptures = applyFilter(state.captures);
  state.selectedId = state.filteredCaptures[0]?.id || null;
  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.toggle("active", item.dataset.filter === filter);
  });
  elements.title.textContent = titleForFilter(filter);
  renderAll();
}

function titleForFilter(filter) {
  const titles = {
    all: state.selectedId || "Winshots captures",
    codex: "Codex-ready captures",
    context: "Captures with context",
    images: "Captures with screenshots"
  };

  return titles[filter] || "Winshots captures";
}

function renderAll() {
  renderMetrics();
  renderRows();
  renderProjects();
  renderRecent();
  renderInspector();
}

function bindEvents() {
  document.addEventListener("click", async (event) => {
    const captureButton = event.target.closest("[data-capture-id]");
    if (captureButton) {
      updateSelection(captureButton.dataset.captureId);
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
        if (inspectorAction.dataset.action === "open-folder") {
          await window.winshots.openCaptureFolder(state.selectedId);
          elements.sync.textContent = "Opened capture folder";
        }
        if (inspectorAction.dataset.action === "copy-image") {
          await window.winshots.copyScreenshotImage(state.selectedId);
          elements.sync.textContent = "Image copied";
        }
        if (inspectorAction.dataset.action === "copy-path") {
          await window.winshots.copyScreenshotPath(state.selectedId);
          elements.sync.textContent = "Image path copied";
        }
        if (inspectorAction.dataset.action === "copy-context") {
          await window.winshots.copyCaptureContext(state.selectedId);
          elements.sync.textContent = "Context copied";
        }
        if (inspectorAction.dataset.action === "copy-metadata") {
          await window.winshots.copyCaptureMetadata(state.selectedId);
          elements.sync.textContent = "Metadata copied";
        }
        if (inspectorAction.dataset.action === "trash") {
          const captureId = state.selectedId;
          if (!window.confirm(`Move capture ${captureId} to the Recycle Bin?`)) {
            return;
          }

          const response = await window.winshots.trashCapture(captureId);
          state.root = response.root;
          state.source = response.source;
          state.captures = response.captures || [];
          state.filteredCaptures = applyFilter(state.captures);
          state.selectedId = state.filteredCaptures[0]?.id || null;
          elements.title.textContent = titleForFilter(state.filter);
          elements.sync.textContent = "Capture moved to Recycle Bin";
          renderAll();
        }
      } catch (error) {
        elements.sync.textContent = error.message || "Capture action failed";
      }
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
  bindEvents();

  try {
    const response = await window.winshots.listCaptures();
    state.root = response.root;
    state.source = response.source;
    state.captures = response.captures || [];
    state.filteredCaptures = applyFilter(state.captures);
    state.selectedId = state.filteredCaptures[1]?.id || state.filteredCaptures[0]?.id || null;
    elements.title.textContent = state.selectedId || "Winshots captures";
    elements.sync.textContent = state.captures.length > 0 ? "Synced just now" : "No local captures found";
    renderAll();
  } catch (error) {
    elements.sync.textContent = "Capture load failed";
    elements.rows.innerHTML = `<div class="empty-state"><strong>Capture load failed</strong><span>${escapeHtml(error.message)}</span></div>`;
  }

  await waitForImages();
  requestAnimationFrame(() => window.winshots.notifyReady());
}

init();
