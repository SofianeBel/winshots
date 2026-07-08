const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { app, BrowserWindow, clipboard, ipcMain, nativeImage, shell } = require("electron");

const rendererPath = path.join(__dirname, "renderer", "index.html");
const isSmoke = process.argv.includes("--smoke");
const screenshotPath = readArgValue("--screenshot");

let mainWindow;
let smokeTimer;
let automationFinished = false;
let screenshotInProgress = false;
let screenshotAttempts = 0;

function readArgValue(name) {
  const inline = process.argv.find((arg) => arg.startsWith(`${name}=`));
  if (inline) {
    return inline.slice(name.length + 1);
  }

  const index = process.argv.indexOf(name);
  if (index >= 0 && process.argv[index + 1]) {
    return process.argv[index + 1];
  }

  return null;
}

if (isSmoke || screenshotPath) {
  app.disableHardwareAcceleration();
  app.commandLine.appendSwitch("disable-gpu");
  app.commandLine.appendSwitch("force-device-scale-factor", "1");
}

function resolveCaptureRoot() {
  if (process.env.WINSHOTS_CAPTURE_ROOT) {
    return path.resolve(process.env.WINSHOTS_CAPTURE_ROOT);
  }

  return path.join(app.getPath("documents"), "Winshots", "captures");
}

function isUnderRoot(root, targetPath) {
  const normalizedRoot = path.resolve(root).toLowerCase();
  const normalizedTarget = path.resolve(targetPath).toLowerCase();
  return normalizedTarget === normalizedRoot || normalizedTarget.startsWith(`${normalizedRoot}${path.sep}`);
}

function assertUnderRoot(root, targetPath, label) {
  if (!targetPath || !isUnderRoot(root, targetPath)) {
    throw new Error(`${label} is not under the Winshots capture root.`);
  }

  return targetPath;
}

function safeReadJson(filePath) {
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return null;
  }
}

function safeReadText(filePath, maxCharacters = 1600) {
  try {
    const text = fs.readFileSync(filePath, "utf8");
    return text.length > maxCharacters ? `${text.slice(0, maxCharacters).trimEnd()}...` : text;
  } catch {
    return "";
  }
}

function hashFile(filePath) {
  try {
    const hash = crypto.createHash("sha1");
    hash.update(fs.readFileSync(filePath));
    return hash.digest("hex");
  } catch {
    return "";
  }
}

function fileSize(filePath) {
  try {
    return fs.statSync(filePath).size;
  } catch {
    return 0;
  }
}

function normalizeCapture(metadata, root) {
  const id = metadata.Id || metadata.id;
  const screenshotPath = metadata.ScreenshotPath || metadata.screenshotPath || "";
  const textPath = metadata.TextPath || metadata.textPath || "";
  const directoryPath = screenshotPath ? path.dirname(screenshotPath) : path.join(root, id || "");
  const metadataPath = path.join(directoryPath, "metadata.json");
  const size = fileSize(screenshotPath);
  const bounds = metadata.Bounds || metadata.bounds || {};
  const metrics = metadata.Metrics || metadata.metrics || {};
  const hash = size > 0 ? hashFile(screenshotPath) : "";

  return {
    id,
    timestampUtc: metadata.TimestampUtc || metadata.timestampUtc || "",
    timestampLocal: metadata.TimestampLocal || metadata.timestampLocal || "",
    reason: metadata.Reason || metadata.reason || "capture",
    windowTitle: metadata.WindowTitle || metadata.windowTitle || "Untitled window",
    processName: metadata.ProcessName || metadata.processName || "unknown",
    processId: metadata.ProcessId || metadata.processId || null,
    windowHandle: metadata.WindowHandle || metadata.windowHandle || "",
    bounds,
    metrics,
    extractedTextLength: metadata.ExtractedTextLength || metadata.extractedTextLength || 0,
    directoryPath,
    screenshotPath,
    textPath,
    metadataPath,
    screenshotUrl: screenshotPath && fs.existsSync(screenshotPath) ? pathToFileURL(screenshotPath).href : "",
    contextPreview: textPath ? safeReadText(textPath, 900) : "",
    screenshotBytes: metrics.ScreenshotBytes || metrics.screenshotBytes || size,
    hash,
    resolution: bounds.Width && bounds.Height ? `${bounds.Width}x${bounds.Height}` : "Unknown"
  };
}

function readIndexedCaptures(root) {
  const indexPath = path.join(root, "index.jsonl");
  if (!fs.existsSync(indexPath)) {
    return [];
  }

  return fs
    .readFileSync(indexPath, "utf8")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      try {
        return JSON.parse(line);
      } catch {
        return null;
      }
    })
    .filter(Boolean);
}

function readScannedCaptures(root) {
  if (!fs.existsSync(root)) {
    return [];
  }

  return fs
    .readdirSync(root, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => safeReadJson(path.join(root, entry.name, "metadata.json")))
    .filter(Boolean);
}

function listCaptures(maxCount = 60) {
  const root = resolveCaptureRoot();
  const fromIndex = readIndexedCaptures(root);
  const records = fromIndex.length > 0 ? fromIndex : readScannedCaptures(root);
  const captures = records
    .map((metadata) => normalizeCapture(metadata, root))
    .filter((capture) => capture.id && isUnderRoot(root, capture.directoryPath))
    .sort((a, b) => String(b.timestampUtc).localeCompare(String(a.timestampUtc)))
    .slice(0, maxCount);

  return {
    root,
    source: fromIndex.length > 0 ? "index.jsonl" : "metadata scan",
    captures
  };
}

function findCapture(captureId) {
  const { root, captures } = listCaptures(100);
  const capture = captures.find((item) => item.id === captureId);
  if (!capture || !isUnderRoot(root, capture.directoryPath)) {
    throw new Error("Capture was not found under the Winshots capture root.");
  }

  return { root, capture };
}

function removeCaptureFromIndex(root, captureId) {
  const indexPath = path.join(root, "index.jsonl");
  if (!fs.existsSync(indexPath)) {
    return;
  }

  const lines = fs.readFileSync(indexPath, "utf8").split(/\r?\n/);
  const kept = lines.filter((line) => {
    if (!line.trim()) {
      return false;
    }

    try {
      const record = JSON.parse(line);
      return (record.Id || record.id) !== captureId;
    } catch {
      return true;
    }
  });

  fs.writeFileSync(indexPath, kept.length > 0 ? `${kept.join("\n")}\n` : "", "utf8");
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1672,
    height: 941,
    minWidth: 1180,
    minHeight: 720,
    show: !isSmoke,
    frame: false,
    backgroundColor: "#101113",
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: path.join(__dirname, "preload.cjs")
    }
  });

  mainWindow.webContents.on("did-fail-load", (_event, errorCode, errorDescription) => {
    console.error(`Electron failed to load renderer: ${errorCode} ${errorDescription}`);
    if (isSmoke || screenshotPath) {
      app.exit(1);
    }
  });
  mainWindow.webContents.on("did-finish-load", () => {
    if (screenshotPath) {
      setTimeout(captureScreenshotAndExit, 7_000);
    }
  });
  mainWindow.webContents.on("render-process-gone", (_event, details) => {
    console.error(`Electron renderer exited: ${details.reason}`);
    if (isSmoke || screenshotPath) {
      app.exit(1);
    }
  });

  mainWindow.loadFile(rendererPath);

  if (isSmoke || screenshotPath) {
    smokeTimer = setTimeout(() => {
      console.error("Electron UI did not become ready within 10 seconds.");
      app.exit(1);
    }, 10_000);
  }
}

async function captureScreenshotAndExit() {
  if (!screenshotPath || !mainWindow || automationFinished || screenshotInProgress) {
    return;
  }

  screenshotInProgress = true;
  screenshotAttempts += 1;

  try {
    const outputPath = path.resolve(screenshotPath);
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    const image = await mainWindow.webContents.capturePage();
    fs.writeFileSync(outputPath, image.toPNG());
    automationFinished = true;
    if (smokeTimer) {
      clearTimeout(smokeTimer);
    }
    app.exit(0);
  } catch (error) {
    screenshotInProgress = false;
    if (screenshotAttempts < 3) {
      setTimeout(captureScreenshotAndExit, 750);
      return;
    }

    if (smokeTimer) {
      clearTimeout(smokeTimer);
    }
    console.error(`Electron screenshot failed: ${error.message}`);
    app.exit(1);
  }
}

ipcMain.handle("captures:list", () => listCaptures());

ipcMain.handle("captures:context", (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const textPath = capture.textPath ? assertUnderRoot(root, capture.textPath, "Context") : "";
  return {
    id: capture.id,
    context: textPath ? safeReadText(textPath, 40_000) : ""
  };
});

ipcMain.handle("captures:open-folder", (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const targetPath = capture.screenshotPath
    ? assertUnderRoot(root, capture.screenshotPath, "Screenshot")
    : capture.directoryPath;
  shell.showItemInFolder(targetPath);
  return true;
});

ipcMain.handle("captures:copy-path", (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const targetPath = capture.screenshotPath
    ? assertUnderRoot(root, capture.screenshotPath, "Screenshot")
    : capture.directoryPath;
  clipboard.writeText(targetPath);
  return true;
});

ipcMain.handle("captures:copy-image", (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const screenshotPath = assertUnderRoot(root, capture.screenshotPath, "Screenshot");
  const image = nativeImage.createFromPath(screenshotPath);
  if (image.isEmpty()) {
    throw new Error("Screenshot image is not available.");
  }

  clipboard.writeImage(image);
  return true;
});

ipcMain.handle("captures:copy-context", (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const textPath = assertUnderRoot(root, capture.textPath, "Context");
  const context = fs.readFileSync(textPath, "utf8");
  clipboard.writeText(context);
  return { characters: context.length };
});

ipcMain.handle("captures:copy-metadata", (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const metadataPath = assertUnderRoot(root, capture.metadataPath, "Metadata");
  const metadata = fs.readFileSync(metadataPath, "utf8");
  clipboard.writeText(metadata);
  return { characters: metadata.length };
});

ipcMain.handle("captures:trash", async (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const directoryPath = assertUnderRoot(root, capture.directoryPath, "Capture folder");
  if (path.resolve(directoryPath) === path.resolve(root)) {
    throw new Error("Refusing to delete the capture root.");
  }

  await shell.trashItem(directoryPath);
  removeCaptureFromIndex(root, capture.id);
  return listCaptures();
});

ipcMain.handle("window:minimize", () => mainWindow?.minimize());
ipcMain.handle("window:maximize", () => {
  if (!mainWindow) {
    return false;
  }

  if (mainWindow.isMaximized()) {
    mainWindow.unmaximize();
  } else {
    mainWindow.maximize();
  }

  return mainWindow.isMaximized();
});
ipcMain.handle("window:close", () => mainWindow?.close());

ipcMain.on("renderer:ready", async () => {
  if (smokeTimer) {
    clearTimeout(smokeTimer);
  }

  if (screenshotPath) {
    await new Promise((resolve) => setTimeout(resolve, 250));
    await captureScreenshotAndExit();
    return;
  }

  if (isSmoke) {
    app.exit(0);
  }
});

app.whenReady().then(createWindow);
app.on("window-all-closed", () => app.quit());
