const crypto = require("node:crypto");
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const net = require("node:net");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { app, BrowserWindow, clipboard, ipcMain, nativeImage, shell } = require("electron");

const rendererPath = path.join(__dirname, "renderer", "index.html");
const isSmoke = process.argv.includes("--smoke");
const screenshotPath = readArgValue("--screenshot");
const screenshotMode = readArgValue("--screenshot-mode") || "main";
const hostPipeName = process.env.WINSHOTS_HOST_PIPE || "";
const automationMode = readArgValue("--automation");
const automationOutputPath = readArgValue("--automation-output");
const automationTargetProcess = readArgValue("--automation-target-process") || "brave";
const isAutomation = Boolean(automationMode);
const automationHoldMs = Math.min(60_000, Math.max(0, readNumberArg("--automation-hold-ms", 0)));
const automationTimeoutMs = Math.min(180_000, Math.max(30_000, readNumberArg("--automation-timeout-ms", 120_000)));

let mainWindow;
let smokeTimer;
let automationTimer;
let automationFinished = false;
let screenshotInProgress = false;
let screenshotAttempts = 0;
let activeSession = null;
const fileHashCache = new Map();

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

function readNumberArg(name, fallback) {
  const value = Number(readArgValue(name));
  return Number.isFinite(value) ? value : fallback;
}

if (isSmoke || screenshotPath || isAutomation) {
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

function resolveSessionRoot() {
  if (process.env.WINSHOTS_SESSION_ROOT) {
    return path.resolve(process.env.WINSHOTS_SESSION_ROOT);
  }

  return path.join(app.getPath("documents"), "Winshots", "sessions");
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function hostUnavailableMessage() {
  return "Winshots host is not available. Start Winshots with Winshots.cmd or Winshots.App.exe to use capture, Codex, timeline, and session actions.";
}

function requestHost(command, payload = {}, timeoutMs = 45_000) {
  if (!hostPipeName) {
    return Promise.reject(new Error(hostUnavailableMessage()));
  }

  const pipePath = `\\\\.\\pipe\\${hostPipeName}`;
  return new Promise((resolve, reject) => {
    const client = net.createConnection(pipePath);
    let output = "";
    let settled = false;
    const timer = setTimeout(() => {
      settleError(new Error("Winshots host command timed out."));
    }, timeoutMs);

    function settleError(error) {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timer);
      client.destroy();
      reject(error);
    }

    function settleResponse() {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timer);
      const line = output
        .split(/\r?\n/)
        .map((item) => item.trim())
        .filter(Boolean)
        .pop();

      if (!line) {
        reject(new Error("Winshots host returned an empty response."));
        return;
      }

      try {
        const response = JSON.parse(line);
        if (!response.ok) {
          reject(new Error(response.error || "Winshots host command failed."));
          return;
        }

        resolve(response.result);
      } catch (error) {
        reject(error);
      }
    }

    client.setEncoding("utf8");
    client.on("connect", () => {
      client.write(`${JSON.stringify({ command, ...payload })}\n`);
    });
    client.on("data", (chunk) => {
      output += chunk;
    });
    client.on("error", settleError);
    client.on("close", settleResponse);
  });
}

function resolveAppCommand() {
  if (process.env.WINSHOTS_APP_PATH) {
    return {
      file: path.resolve(process.env.WINSHOTS_APP_PATH),
      prefix: [],
      cwd: path.dirname(path.resolve(process.env.WINSHOTS_APP_PATH))
    };
  }

  const packagedExe = path.resolve(__dirname, "..", "app", "Winshots.App.exe");
  if (fs.existsSync(packagedExe)) {
    return { file: packagedExe, prefix: [], cwd: path.dirname(packagedExe) };
  }

  const builtExeCandidates = [
    path.resolve(__dirname, "..", "Winshots.App", "bin", "Release", "net8.0-windows", "Winshots.App.exe"),
    path.resolve(__dirname, "..", "Winshots.App", "bin", "Debug", "net8.0-windows", "Winshots.App.exe")
  ];
  const builtExe = builtExeCandidates.find((candidate) => fs.existsSync(candidate));
  if (builtExe) {
    return { file: builtExe, prefix: [], cwd: path.dirname(builtExe) };
  }

  const projectPath = path.resolve(__dirname, "..", "Winshots.App", "Winshots.App.csproj");
  if (fs.existsSync(projectPath)) {
    return {
      file: "dotnet",
      prefix: ["run", "--project", projectPath, "--"],
      cwd: path.resolve(__dirname, "..", "..")
    };
  }

  throw new Error("Winshots C# app command was not found.");
}

function runAppCommand(commandArgs, options = {}) {
  const command = resolveAppCommand();
  const child = spawn(command.file, [...command.prefix, ...commandArgs], {
    cwd: command.cwd,
    env: process.env,
    windowsHide: true
  });

  return collectChildResult(child, options.timeoutMs || 30_000);
}

function collectChildResult(child, timeoutMs, timeoutMessage = "Winshots app command timed out.") {
  return new Promise((resolve, reject) => {
    let stdout = "";
    let stderr = "";
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) {
        return;
      }

      settled = true;
      child.kill();
      reject(new Error(timeoutMessage));
    }, timeoutMs);

    child.stdout?.on("data", (chunk) => {
      stdout += chunk.toString();
    });
    child.stderr?.on("data", (chunk) => {
      stderr += chunk.toString();
    });
    child.on("error", (error) => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timer);
      reject(error);
    });
    child.on("close", (code) => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timer);
      if (code === 0) {
        resolve({ stdout: stdout.trim(), stderr: stderr.trim() });
        return;
      }

      reject(new Error((stderr || stdout || `Winshots app command failed with exit code ${code}.`).trim()));
    });
  });
}

function defaultInstallRoot() {
  const localAppData = process.env.LOCALAPPDATA || path.join(app.getPath("home"), "AppData", "Local");
  return path.join(localAppData, "Programs", "Winshots");
}

function isReleasePackageRoot(candidate) {
  return fs.existsSync(path.join(candidate, "install.ps1")) &&
    fs.existsSync(path.join(candidate, "app", "Winshots.App.exe")) &&
    fs.existsSync(path.join(candidate, "mcp", "Winshots.Mcp.exe"));
}

function resolvePackageRoot() {
  const candidates = [
    path.resolve(__dirname, ".."),
    path.resolve(process.cwd())
  ];

  for (const candidate of candidates) {
    if (isReleasePackageRoot(candidate)) {
      return candidate;
    }
  }

  return path.resolve(__dirname, "..", "..");
}

function readPackageInfo() {
  const packageRoot = resolvePackageRoot();
  const installRoot = defaultInstallRoot();
  const hasInstallScript = fs.existsSync(path.join(packageRoot, "install.ps1"));
  const hasElectronRuntime = fs.existsSync(path.join(packageRoot, "electron-runtime", "electron.exe"));
  const hasMcpServer = fs.existsSync(path.join(packageRoot, "mcp", "Winshots.Mcp.exe"));
  const hasApp = fs.existsSync(path.join(packageRoot, "app", "Winshots.App.exe"));
  const installed = isUnderRoot(installRoot, packageRoot);
  const installTargetReady = isReleasePackageRoot(installRoot);
  const releasePackage = hasInstallScript && hasMcpServer && hasApp;
  const mode = installed ? "installed" : releasePackage ? "portable" : "source";

  return {
    mode,
    packageRoot,
    installRoot,
    hasInstallScript,
    hasElectronRuntime,
    hasMcpServer,
    hasApp,
    installTargetReady,
    canInstall: releasePackage && !installed,
    starts: [
      "C# host",
      "Electron review UI",
      "global shortcuts",
      "overlay/session controls",
      "local MCP files"
    ]
  };
}

async function runPackageInstall() {
  const info = readPackageInfo();
  if (!info.canInstall) {
    throw new Error("This Winshots build cannot be installed from the current folder.");
  }

  const installScript = path.join(info.packageRoot, "install.ps1");
  const powershell = path.join(process.env.SystemRoot || "C:\\Windows", "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
  const child = spawn(powershell, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", installScript], {
    cwd: info.packageRoot,
    windowsHide: true
  });

  const result = await collectChildResult(child, 180_000, "Winshots local install timed out.");
  return {
    ...result,
    info: readPackageInfo()
  };
}

function parseLastJsonLine(output) {
  const lines = String(output || "")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

  for (let index = lines.length - 1; index >= 0; index--) {
    try {
      return JSON.parse(lines[index]);
    } catch {
      // Non-JSON output can come from dotnet build/run noise.
    }
  }

  return null;
}

async function runCaptureCommand(options = {}) {
  const pasteToCodex = Boolean(options.pasteToCodex);
  const reason = options.reason || (pasteToCodex ? "electron-codex" : "electron");

  const command = await requestHost(
    "capture",
    {
      pasteToCodex,
      reason
    },
    pasteToCodex ? 60_000 : 30_000
  );

  return {
    command,
    stderr: "",
    ...listCaptures()
  };
}

function createSessionStopFilePath() {
  return path.join(app.getPath("temp"), `winshots-electron-session-${Date.now()}-${process.pid}.stop`);
}

async function startVisualSession(options = {}) {
  if (activeSession) {
    throw new Error("A Winshots visual session is already running.");
  }

  const intervalMs = Math.max(1000, Number(options.intervalMs || 1000));
  const durationSeconds = Math.max(1, Number(options.durationSeconds || 300));

  const result = await requestHost(
    "session.start",
    {
      intervalMs,
      durationSeconds
    },
    30_000
  );
  activeSession = result?.Running ? { host: true } : null;
  return result;
}

async function stopVisualSession() {
  if (!activeSession) {
    return { running: false, manifest: null };
  }

  const response = await requestHost("session.stop", {}, 90_000);
  activeSession = null;
  return {
    running: false,
    manifest: response?.Manifest || null,
    stderr: ""
  };
}

async function toggleTimelineCommand(options = {}) {
  const intervalMs = Math.max(1000, Number(options.intervalMs || 1000));
  return requestHost("timeline.toggle", { intervalMs }, 30_000);
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

async function readText(filePath, maxCharacters = 1600) {
  try {
    const text = await fs.promises.readFile(filePath, "utf8");
    return text.length > maxCharacters ? `${text.slice(0, maxCharacters).trimEnd()}...` : text;
  } catch {
    return "";
  }
}

async function hashFile(filePath) {
  try {
    const stats = await fs.promises.stat(filePath);
    const signature = `${stats.size}:${stats.mtimeMs}`;
    const cached = fileHashCache.get(filePath);
    if (cached?.signature === signature) {
      return cached.hash;
    }

    const hash = await new Promise((resolve, reject) => {
      const digest = crypto.createHash("sha1");
      const input = fs.createReadStream(filePath);
      input.on("data", (chunk) => digest.update(chunk));
      input.on("error", reject);
      input.on("end", () => resolve(digest.digest("hex")));
    });
    fileHashCache.set(filePath, { signature, hash });
    return hash;
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
    contextPreview: "",
    screenshotBytes: metrics.ScreenshotBytes || metrics.screenshotBytes || size,
    hash: "",
    resolution: bounds.Width && bounds.Height ? `${bounds.Width}x${bounds.Height}` : "Unknown"
  };
}

function readIndexedCaptures(root, maxCount) {
  const indexPath = path.join(root, "index.jsonl");
  if (!fs.existsSync(indexPath)) {
    return [];
  }

  return fs
    .readFileSync(indexPath, "utf8")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .slice(-maxCount)
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
  const fromIndex = readIndexedCaptures(root, maxCount);
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
    icon: path.join(__dirname, "assets", "winshots.ico"),
    backgroundColor: "#101113",
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: path.join(__dirname, "preload.cjs")
    }
  });

  mainWindow.webContents.on("did-fail-load", (_event, errorCode, errorDescription) => {
    console.error(`Electron failed to load renderer: ${errorCode} ${errorDescription}`);
    if (isAutomation) {
      finishAutomationAndExit(
        automationEvidence("did-fail-load", {
          error: `Electron failed to load renderer: ${errorCode} ${errorDescription}`
        }),
        1
      );
      return;
    }

    if (isSmoke || screenshotPath) {
      app.exit(1);
    }
  });
  mainWindow.webContents.on("did-finish-load", () => {
    if (screenshotPath) {
      setTimeout(async () => {
        await prepareScreenshotMode();
        await captureScreenshotAndExit();
      }, 7_000);
      return;
    }

    if (automationMode === "session") {
      setTimeout(() => runSessionAutomationAndExit(), 250);
      return;
    }

    if (automationMode === "to-codex") {
      setTimeout(() => runToCodexAutomationAndExit(), 250);
      return;
    }

    if (automationMode) {
      finishAutomationAndExit(
        automationEvidence("unsupported-automation", {
          error: `Unsupported Electron automation mode: ${automationMode}`
        }),
        1
      );
    }
  });
  mainWindow.webContents.on("render-process-gone", (_event, details) => {
    console.error(`Electron renderer exited: ${details.reason}`);
    if (isAutomation) {
      finishAutomationAndExit(
        automationEvidence("render-process-gone", {
          error: `Electron renderer exited: ${details.reason}`
        }),
        1
      );
      return;
    }

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

  if (isAutomation) {
    automationTimer = setTimeout(() => {
      finishAutomationAndExit(
        automationEvidence("load-timeout", {
          error: `Electron automation did not finish within ${automationTimeoutMs}ms.`
        }),
        1
      );
    }, automationTimeoutMs);
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

async function prepareScreenshotMode() {
  if (!mainWindow) {
    return;
  }

  const selectorByMode = {
    grid: '[data-view-mode="grid"]',
    preview: "[data-open-preview]",
    settings: "[data-settings-open]"
  };
  const selector = selectorByMode[screenshotMode];
  if (!selector) {
    return;
  }

  if (screenshotMode === "settings") {
    await mainWindow.webContents.executeJavaScript(`
      new Promise((resolve) => {
        if (typeof openSettings === "function") {
          openSettings();
        } else {
          document.querySelector(${JSON.stringify(selector)})?.click();
        }
        setTimeout(resolve, 650);
      });
    `);
    return;
  }

  await mainWindow.webContents.executeJavaScript(`
    new Promise((resolve) => {
      const trigger = document.querySelector(${JSON.stringify(selector)});
      if (trigger) {
        trigger.click();
      }
      setTimeout(resolve, 450);
    });
  `);
}

function writeAutomationEvidence(evidence) {
  const body = `${JSON.stringify(evidence, null, 2)}\n`;
  if (!automationOutputPath) {
    console.log(body.trimEnd());
    return;
  }

  const outputPath = path.resolve(automationOutputPath);
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, body, "utf8");
}

function automationEvidence(stage, values = {}) {
  return {
    mode: automationMode,
    method: "main-process-dom-click",
    stage,
    args: process.argv.slice(2),
    hostPipeAvailable: Boolean(hostPipeName),
    startedAtUtc: new Date().toISOString(),
    ok: false,
    selectorFound: false,
    ui: {},
    host: {},
    cleanup: null,
    ...values
  };
}

function finishAutomationAndExit(evidence, exitCode) {
  if (automationFinished) {
    return;
  }

  automationFinished = true;
  if (automationTimer) {
    clearTimeout(automationTimer);
  }
  if (smokeTimer) {
    clearTimeout(smokeTimer);
  }

  writeAutomationEvidence({
    ...evidence,
    finishedAtUtc: new Date().toISOString()
  });
  app.exit(exitCode);
}

function withTimeout(promise, timeoutMs, stage) {
  let timer;
  return Promise.race([
    promise,
    new Promise((_, reject) => {
      timer = setTimeout(() => {
        const error = new Error(`${stage} timed out after ${timeoutMs}ms.`);
        error.stage = stage;
        reject(error);
      }, timeoutMs);
    })
  ]).finally(() => clearTimeout(timer));
}

function normalizeHostStatus(status, stage) {
  return {
    stage,
    sessionRunning: Boolean(status?.SessionRunning ?? status?.sessionRunning),
    timelineRunning: Boolean(status?.TimelineRunning ?? status?.timelineRunning),
    overlayVisible: Boolean(status?.OverlayVisible ?? status?.overlayVisible),
    overlayText: String(status?.OverlayText ?? status?.overlayText ?? "")
  };
}

async function readHostStatus(stage) {
  try {
    const status = await requestHost("status", {}, 5_000);
    return normalizeHostStatus(status, stage);
  } catch (error) {
    return {
      stage,
      error: error.message || String(error)
    };
  }
}

async function readSessionDomSnapshot(stage) {
  if (!mainWindow) {
    throw new Error("Electron main window is not available.");
  }

  const script = `
    (() => {
      const button = document.querySelector('[data-command="session"]');
      const status = document.querySelector('#sync-status');
      const labels = [...document.querySelectorAll('[data-command]')].map((node) => ({
        command: node.getAttribute("data-command") || "",
        text: (node.textContent || "").replace(/\\s+/g, " ").trim(),
        disabled: Boolean(node.disabled),
        ariaPressed: node.getAttribute("aria-pressed") || ""
      }));

      return {
        stage: ${JSON.stringify(stage)},
        selectorFound: Boolean(button),
        labels,
        buttonText: (button?.textContent || "").replace(/\\s+/g, " ").trim(),
        ariaPressed: button?.getAttribute("aria-pressed") || "",
        disabled: Boolean(button?.disabled),
        statusText: (status?.textContent || "").trim()
      };
    })();
  `;

  return await withTimeout(mainWindow.webContents.executeJavaScript(script), 5_000, stage);
}

async function clickSessionButtonAndWait(stage, timeoutMs) {
  if (!mainWindow) {
    throw new Error("Electron main window is not available.");
  }

  const isStart = stage === "start";
  const predicate = isStart
    ? `state.buttonText === "Stop session" && state.statusText === "Session recording"`
    : `state.buttonText === "Session" && state.statusText !== "Session recording"`;
  const script = `
    (async () => {
      const button = document.querySelector('[data-command="session"]');
      const status = document.querySelector('#sync-status');
      const labels = () => [...document.querySelectorAll('[data-command]')].map((node) => ({
        command: node.getAttribute("data-command") || "",
        text: (node.textContent || "").replace(/\\s+/g, " ").trim(),
        disabled: Boolean(node.disabled),
        ariaPressed: node.getAttribute("aria-pressed") || ""
      }));
      const snapshot = () => ({
        selectorFound: Boolean(button),
        labels: labels(),
        buttonText: (button?.textContent || "").replace(/\\s+/g, " ").trim(),
        ariaPressed: button?.getAttribute("aria-pressed") || "",
        disabled: Boolean(button?.disabled),
        statusText: (status?.textContent || "").trim()
      });
      const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
      const waitFor = async () => {
        const started = Date.now();
        while (Date.now() - started < ${JSON.stringify(timeoutMs)}) {
          const state = snapshot();
          if (${predicate}) {
            return { ok: true, elapsedMs: Date.now() - started, state };
          }
          await sleep(100);
        }
        return { ok: false, elapsedMs: Date.now() - started, state: snapshot() };
      };

      const beforeClick = snapshot();
      if (!button) {
        return { stage: ${JSON.stringify(stage)}, clicked: false, beforeClick, transition: { ok: false, state: beforeClick } };
      }

      button.click();
      return {
        stage: ${JSON.stringify(stage)},
        clicked: true,
        beforeClick,
        transition: await waitFor(),
        afterClick: snapshot()
      };
    })();
  `;

  return await withTimeout(mainWindow.webContents.executeJavaScript(script), timeoutMs + 5_000, stage);
}

function isRecordingOverlayStatus(status) {
  return Boolean(status?.sessionRunning && status?.overlayVisible && status?.overlayText === "Recording session");
}

function isStoppedHostStatus(status) {
  return Boolean(status && !status.sessionRunning && !status.overlayVisible);
}

async function cleanupSessionIfNeeded(evidence) {
  evidence.cleanup = {
    attempted: false,
    method: null,
    error: null
  };

  const cleanupBefore = await readHostStatus("cleanup-before");
  evidence.host.cleanupBefore = cleanupBefore;

  let domBefore = null;
  try {
    domBefore = await readSessionDomSnapshot("cleanup-before");
    evidence.ui.cleanupBefore = domBefore;
  } catch (error) {
    evidence.ui.cleanupBefore = { error: error.message || String(error) };
  }

  if (domBefore?.buttonText === "Stop session") {
    evidence.cleanup.attempted = true;
    evidence.cleanup.method = "dom-click";
    try {
      evidence.cleanup.result = await clickSessionButtonAndWait("cleanup-stop", 45_000);
    } catch (error) {
      evidence.cleanup.error = error.message || String(error);
    }
  } else if (cleanupBefore.sessionRunning) {
    evidence.cleanup.attempted = true;
    evidence.cleanup.method = "host-session.stop";
    try {
      evidence.cleanup.result = await requestHost("session.stop", {}, 45_000);
    } catch (error) {
      evidence.cleanup.error = error.message || String(error);
    }
  }

  evidence.host.cleanupAfter = await readHostStatus("cleanup-after");
  try {
    evidence.ui.cleanupAfter = await readSessionDomSnapshot("cleanup-after");
  } catch (error) {
    evidence.ui.cleanupAfter = { error: error.message || String(error) };
  }
}

async function runSessionAutomationAndExit() {
  if (automationTimer) {
    clearTimeout(automationTimer);
    automationTimer = null;
  }

  if (!mainWindow) {
    finishAutomationAndExit(
      automationEvidence("window-missing", { error: "Electron main window is not available." }),
      1
    );
    return;
  }

  const evidence = automationEvidence("load");
  try {
    evidence.stage = "before-start";
    evidence.ui.beforeStart = await readSessionDomSnapshot("before-start");
    evidence.selectorFound = evidence.ui.beforeStart.selectorFound;
    evidence.host.beforeStart = await readHostStatus("before-start");
    if (!evidence.selectorFound) {
      throw new Error("Session command button was not found.");
    }

    evidence.stage = "start";
    evidence.ui.afterStart = await clickSessionButtonAndWait("start", 30_000);
    evidence.host.duringStart = await readHostStatus("during-start");
    if (!evidence.ui.afterStart?.transition?.ok) {
      throw new Error("Session UI did not transition to Stop session / Session recording.");
    }
    if (!isRecordingOverlayStatus(evidence.host.duringStart)) {
      throw new Error("Session started in the UI, but the host overlay was not visible as Recording session.");
    }

    evidence.stage = "hold";
    if (automationHoldMs > 0) {
      await delay(automationHoldMs);
    }
    evidence.ui.afterHold = await readSessionDomSnapshot("after-hold");
    evidence.host.afterHold = await readHostStatus("after-hold");

    evidence.stage = "stop";
    evidence.ui.afterStop = await clickSessionButtonAndWait("stop", 90_000);
    evidence.host.afterStop = await readHostStatus("after-stop");
    if (!evidence.ui.afterStop?.transition?.ok) {
      throw new Error("Session UI did not transition back to Session.");
    }
    if (!isStoppedHostStatus(evidence.host.afterStop)) {
      throw new Error("Session stop completed in the UI, but host session/overlay is still active.");
    }

    evidence.stage = "complete";
    evidence.ok = true;
    finishAutomationAndExit(evidence, 0);
  } catch (error) {
    evidence.ok = false;
    evidence.error = error.message || String(error);
    evidence.errorStage = error.stage || evidence.stage;
    await cleanupSessionIfNeeded(evidence);
    finishAutomationAndExit(evidence, 1);
  }
}

async function activateExternalTarget(processName = automationTargetProcess) {
  const script = `
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinshotsAutomationNative {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
    $targetName = ${JSON.stringify(processName)}
    $target = Get-Process -Name $targetName -ErrorAction SilentlyContinue |
      Where-Object { $_.MainWindowHandle -ne 0 -and -not [string]::IsNullOrWhiteSpace($_.MainWindowTitle) } |
      Select-Object -First 1
    if ($null -eq $target) {
      [pscustomobject]@{ found = $false; processName = $targetName; error = "No visible target window found." } | ConvertTo-Json -Compress
      exit 0
    }

    [WinshotsAutomationNative]::ShowWindow($target.MainWindowHandle, 9) | Out-Null
    Start-Sleep -Milliseconds 150
    $activated = [WinshotsAutomationNative]::SetForegroundWindow($target.MainWindowHandle)
    [pscustomobject]@{
      found = $true
      activated = [bool]$activated
      processName = $target.ProcessName
      processId = $target.Id
      windowTitle = $target.MainWindowTitle
      windowHandle = ("0x{0:X}" -f $target.MainWindowHandle.ToInt64())
    } | ConvertTo-Json -Compress
  `;

  const child = spawn("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script], {
    windowsHide: true
  });
  const result = await collectChildResult(child, 8_000);
  return parseLastJsonLine(result.stdout) || { found: false, error: result.stderr || "Target activation returned no JSON." };
}

async function readCommandDomSnapshot(stage, commandName) {
  if (!mainWindow) {
    throw new Error("Electron main window is not available.");
  }

  const selector = `[data-command="${commandName}"]`;
  const script = `
    (() => {
      const selector = ${JSON.stringify(selector)};
      const button = document.querySelector(selector);
      const status = document.querySelector('#sync-status');
      const labels = [...document.querySelectorAll('[data-command]')].map((node) => ({
        command: node.getAttribute("data-command") || "",
        text: (node.textContent || "").replace(/\\s+/g, " ").trim(),
        disabled: Boolean(node.disabled),
        ariaPressed: node.getAttribute("aria-pressed") || ""
      }));

      return {
        stage: ${JSON.stringify(stage)},
        selector,
        selectorFound: Boolean(button),
        labels,
        buttonText: (button?.textContent || "").replace(/\\s+/g, " ").trim(),
        disabled: Boolean(button?.disabled),
        statusText: (status?.textContent || "").trim()
      };
    })();
  `;

  return await withTimeout(mainWindow.webContents.executeJavaScript(script), 5_000, stage);
}

async function clickCommandButtonAndWait(commandName, stage, timeoutMs) {
  if (!mainWindow) {
    throw new Error("Electron main window is not available.");
  }

  const selector = `[data-command="${commandName}"]`;
  const script = `
    (async () => {
      const selector = ${JSON.stringify(selector)};
      const button = document.querySelector(selector);
      const status = document.querySelector('#sync-status');
      const labels = () => [...document.querySelectorAll('[data-command]')].map((node) => ({
        command: node.getAttribute("data-command") || "",
        text: (node.textContent || "").replace(/\\s+/g, " ").trim(),
        disabled: Boolean(node.disabled),
        ariaPressed: node.getAttribute("aria-pressed") || ""
      }));
      const snapshot = () => ({
        selector,
        selectorFound: Boolean(button),
        labels: labels(),
        buttonText: (button?.textContent || "").replace(/\\s+/g, " ").trim(),
        disabled: Boolean(button?.disabled),
        statusText: (status?.textContent || "").trim()
      });
      const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
      const beforeClick = snapshot();
      if (!button) {
        return { stage: ${JSON.stringify(stage)}, clicked: false, beforeClick, transition: { ok: false, state: beforeClick } };
      }

      button.click();
      const startedAt = Date.now();
      let commandStarted = false;
      while (Date.now() - startedAt < ${JSON.stringify(timeoutMs)}) {
        const state = snapshot();
        if (state.disabled || /Capturing for Codex/i.test(state.statusText)) {
          commandStarted = true;
        }
        if (commandStarted && !state.disabled && !/Capturing for Codex/i.test(state.statusText)) {
          return {
            stage: ${JSON.stringify(stage)},
            clicked: true,
            beforeClick,
            transition: { ok: true, elapsedMs: Date.now() - startedAt, state },
            afterClick: snapshot()
          };
        }
        await sleep(150);
      }

      return {
        stage: ${JSON.stringify(stage)},
        clicked: true,
        beforeClick,
        transition: { ok: false, elapsedMs: Date.now() - startedAt, state: snapshot() },
        afterClick: snapshot()
      };
    })();
  `;

  return await withTimeout(mainWindow.webContents.executeJavaScript(script), timeoutMs + 5_000, stage);
}

function latestCaptureSummary(stage) {
  const captures = listCaptures(5).captures;
  const latest = captures[0] || null;
  return {
    stage,
    latestId: latest?.id || null,
    latest
  };
}

async function waitForNewCapture(previousId, timeoutMs) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    const summary = latestCaptureSummary("after-click");
    if (summary.latestId && summary.latestId !== previousId) {
      return {
        ok: true,
        elapsedMs: Date.now() - startedAt,
        ...summary
      };
    }
    await delay(250);
  }

  return {
    ok: false,
    elapsedMs: Date.now() - startedAt,
    ...latestCaptureSummary("after-click-timeout")
  };
}

function readCaptureMetadata(capture) {
  if (!capture?.metadataPath) {
    return null;
  }

  const metadata = safeReadJson(capture.metadataPath);
  if (!metadata) {
    return null;
  }

  return {
    id: metadata.Id || metadata.id || capture.id,
    reason: metadata.Reason || metadata.reason || "",
    windowTitle: metadata.WindowTitle || metadata.windowTitle || "",
    processName: metadata.ProcessName || metadata.processName || "",
    directoryPath: capture.directoryPath,
    screenshotPath: metadata.ScreenshotPath || metadata.screenshotPath || capture.screenshotPath,
    textPath: metadata.TextPath || metadata.textPath || capture.textPath,
    metadataPath: capture.metadataPath
  };
}

function classifyCodexDelivery(statusText) {
  const text = String(statusText || "");
  if (/Attached screenshot\.png, metadata\.json, and context\.txt/i.test(text)) {
    return { status: "attached", ok: true, message: text };
  }

  if (/Capture saved at|Attach screenshot\.png|paste the capture manually|manually|could not confirm|Codex App is not running|no visible window|could not safely identify/i.test(text)) {
    return { status: "fallback", ok: true, message: text };
  }

  return { status: "unproven", ok: false, message: text };
}

function winshotsWindowState() {
  if (!mainWindow || mainWindow.isDestroyed()) {
    return {
      visible: false,
      destroyed: true
    };
  }

  return {
    visible: mainWindow.isVisible(),
    destroyed: false,
    minimized: mainWindow.isMinimized(),
    bounds: mainWindow.getBounds()
  };
}

function isExpectedToCodexCapture(metadata) {
  if (!metadata) {
    return false;
  }

  const processName = metadata.processName.toLowerCase();
  return metadata.reason === "electron-codex" &&
    !["electron", "winshots.app", "winshots", "codex"].includes(processName);
}

async function runToCodexAutomationAndExit() {
  if (automationTimer) {
    clearTimeout(automationTimer);
    automationTimer = null;
  }

  if (!mainWindow) {
    finishAutomationAndExit(
      automationEvidence("window-missing", { error: "Electron main window is not available." }),
      1
    );
    return;
  }

  const evidence = automationEvidence("load", {
    targetProcessRequested: automationTargetProcess,
    delivery: null,
    captureBefore: null,
    captureAfter: null,
    metadata: null,
    winshotsWindowAfter: null
  });

  try {
    evidence.stage = "before-click";
    evidence.ui.beforeClick = await readCommandDomSnapshot("before-click", "capture-codex");
    evidence.selectorFound = evidence.ui.beforeClick.selectorFound;
    evidence.captureBefore = latestCaptureSummary("before-click");
    evidence.targetBeforeClick = await activateExternalTarget();
    await delay(1_200);
    if (!evidence.selectorFound) {
      throw new Error("To Codex command button was not found.");
    }
    if (!evidence.targetBeforeClick?.found) {
      throw new Error(evidence.targetBeforeClick?.error || "No external target was available.");
    }

    evidence.stage = "click";
    evidence.ui.afterClick = await clickCommandButtonAndWait("capture-codex", "click", 90_000);
    evidence.captureAfter = await waitForNewCapture(evidence.captureBefore.latestId, 10_000);
    evidence.ui.afterCapture = await readCommandDomSnapshot("after-capture", "capture-codex");
    evidence.winshotsWindowAfter = winshotsWindowState();
    evidence.metadata = evidence.captureAfter.ok ? readCaptureMetadata(evidence.captureAfter.latest) : null;
    evidence.delivery = classifyCodexDelivery(evidence.ui.afterCapture.statusText);

    if (!evidence.ui.afterClick?.clicked) {
      throw new Error("To Codex command button was not clicked.");
    }
    if (!evidence.ui.afterClick?.transition?.ok) {
      throw new Error("To Codex UI action did not complete.");
    }
    if (!evidence.captureAfter.ok || !evidence.metadata) {
      throw new Error("To Codex did not create a new capture.");
    }
    if (!isExpectedToCodexCapture(evidence.metadata)) {
      throw new Error("To Codex captured the wrong target or reason.");
    }
    if (!evidence.winshotsWindowAfter.visible || evidence.winshotsWindowAfter.destroyed) {
      throw new Error("Winshots window is not visible after To Codex.");
    }
    if (!evidence.delivery?.ok) {
      throw new Error("Codex delivery or manual fallback was not proven.");
    }

    evidence.stage = "complete";
    evidence.ok = true;
    finishAutomationAndExit(evidence, 0);
  } catch (error) {
    evidence.ok = false;
    evidence.error = error.message || String(error);
    evidence.errorStage = error.stage || evidence.stage;
    evidence.winshotsWindowAfter ??= winshotsWindowState();
    finishAutomationAndExit(evidence, 1);
  }
}

ipcMain.handle("captures:list", () => listCaptures());

ipcMain.handle("app:capture", (_event, options) => runCaptureCommand(options));

ipcMain.handle("timeline:toggle", (_event, options) => toggleTimelineCommand(options));

ipcMain.handle("sessions:start", (_event, options) => startVisualSession(options));

ipcMain.handle("sessions:stop", () => stopVisualSession());

ipcMain.handle("captures:context", async (_event, captureId) => {
  const { root, capture } = findCapture(captureId);
  const textPath = capture.textPath ? assertUnderRoot(root, capture.textPath, "Context") : "";
  const screenshotPath = capture.screenshotPath
    ? assertUnderRoot(root, capture.screenshotPath, "Screenshot")
    : "";
  const [context, hash] = await Promise.all([
    textPath ? readText(textPath, 40_000) : "",
    screenshotPath ? hashFile(screenshotPath) : ""
  ]);
  return {
    id: capture.id,
    context,
    hash
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
  fileHashCache.delete(capture.screenshotPath);
  removeCaptureFromIndex(root, capture.id);
  return listCaptures();
});

ipcMain.handle("package:info", () => readPackageInfo());

ipcMain.handle("package:install", () => runPackageInstall());

ipcMain.handle("package:open-folder", async () => {
  const info = readPackageInfo();
  const error = await shell.openPath(info.packageRoot);
  if (error) {
    throw new Error(error);
  }

  return true;
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
    await prepareScreenshotMode();
    await captureScreenshotAndExit();
    return;
  }

  if (isSmoke) {
    app.exit(0);
  }
});

app.whenReady().then(createWindow);
app.on("window-all-closed", () => app.quit());
