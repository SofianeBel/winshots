const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");

function isUnderRoot(root, targetPath) {
  const normalizedRoot = path.resolve(root).toLowerCase();
  const normalizedTarget = path.resolve(targetPath).toLowerCase();
  return normalizedTarget === normalizedRoot || normalizedTarget.startsWith(`${normalizedRoot}${path.sep}`);
}

function safeReadJson(filePath) {
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return null;
  }
}

function safeReadText(filePath, maxCharacters) {
  try {
    const text = fs.readFileSync(filePath, "utf8");
    return text.length > maxCharacters ? `${text.slice(0, maxCharacters).trimEnd()}...` : text;
  } catch {
    return "";
  }
}

function existingSessionPath(root, directoryPath, candidate, fallback) {
  const paths = [candidate, fallback]
    .filter(Boolean)
    .map((value) => path.isAbsolute(value) ? path.resolve(value) : path.resolve(directoryPath, value));
  return paths.find((value) => isUnderRoot(root, value) && fs.existsSync(value)) || "";
}

function frameNumberFromName(fileName, fallback) {
  const parsed = Number(path.parse(fileName).name);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function readFrameRecords(indexPath) {
  if (!fs.existsSync(indexPath)) {
    return [];
  }

  try {
    return fs.readFileSync(indexPath, "utf8")
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
  } catch {
    return [];
  }
}

function scanFrameRecords(directoryPath) {
  const framesPath = path.join(directoryPath, "frames");
  if (!fs.existsSync(framesPath)) {
    return [];
  }

  try {
    return fs.readdirSync(framesPath, { withFileTypes: true })
      .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".png"))
      .sort((a, b) => a.name.localeCompare(b.name))
      .map((entry, index) => ({
        Number: frameNumberFromName(entry.name, index + 1),
        Captured: true,
        ScreenshotPath: path.join(framesPath, entry.name)
      }));
  } catch {
    return [];
  }
}

function normalizeFrame(root, directoryPath, record, fallbackNumber) {
  const number = Number(record.Number ?? record.number) || fallbackNumber;
  const frameId = String(number).padStart(6, "0");
  const screenshotPath = existingSessionPath(
    root,
    directoryPath,
    record.ScreenshotPath || record.screenshotPath,
    path.join(directoryPath, "frames", `${frameId}.png`)
  );
  const textPath = existingSessionPath(
    root,
    directoryPath,
    record.TextPath || record.textPath,
    path.join(directoryPath, "contexts", `${frameId}.txt`)
  );
  const metadataPath = existingSessionPath(
    root,
    directoryPath,
    record.MetadataPath || record.metadataPath,
    path.join(directoryPath, "contexts", `${frameId}.metadata.json`)
  );
  const metadata = metadataPath ? safeReadJson(metadataPath) : null;
  const diagnostics = record.Diagnostics || record.diagnostics || metadata?.Diagnostics || metadata?.diagnostics || null;
  const explicitlyFailed = (record.Captured ?? record.captured) === false;

  return {
    number,
    timestampUtc: record.TimestampUtc || record.timestampUtc || metadata?.TimestampUtc || metadata?.timestampUtc || "",
    timestampLocal: record.TimestampLocal || record.timestampLocal || metadata?.TimestampLocal || metadata?.timestampLocal || "",
    captured: !explicitlyFailed && Boolean(screenshotPath),
    error: record.Error || record.error || (!screenshotPath ? "Screenshot file is missing." : ""),
    windowTitle: record.WindowTitle || record.windowTitle || metadata?.WindowTitle || metadata?.windowTitle || "Unknown window",
    processName: record.ProcessName || record.processName || metadata?.ProcessName || metadata?.processName || "unknown",
    screenshotPath,
    screenshotUrl: screenshotPath ? pathToFileURL(screenshotPath).href : "",
    textPath,
    metadataPath,
    context: textPath ? safeReadText(textPath, 40_000) : "",
    bounds: record.Bounds || record.bounds || metadata?.Bounds || metadata?.bounds || null,
    metrics: record.Metrics || record.metrics || metadata?.Metrics || metadata?.metrics || null,
    diagnostics
  };
}

function normalizeSession(root, directoryPath, manifest = {}) {
  const stats = fs.statSync(directoryPath);
  const id = manifest.Id || manifest.id || path.basename(directoryPath);
  const indexPath = path.join(directoryPath, "frames.jsonl");
  const records = readFrameRecords(indexPath);
  const scannedRecords = records.length > 0 ? records : scanFrameRecords(directoryPath);
  const frames = scannedRecords
    .map((record, index) => normalizeFrame(root, directoryPath, record, index + 1))
    .sort((a, b) => a.number - b.number);
  const videoPath = existingSessionPath(
    root,
    directoryPath,
    manifest.VideoPath || manifest.videoPath,
    path.join(directoryPath, "video.mp4")
  );
  const contextPath = existingSessionPath(
    root,
    directoryPath,
    manifest.ContextPath || manifest.contextPath,
    path.join(directoryPath, "context.md")
  );
  const startedUtc = manifest.StartedUtc || manifest.startedUtc || stats.birthtime.toISOString();

  return {
    id,
    status: manifest.Status || manifest.status || "incomplete",
    startedUtc,
    startedLocal: manifest.StartedLocal || manifest.startedLocal || startedUtc,
    completedUtc: manifest.CompletedUtc || manifest.completedUtc || "",
    completedLocal: manifest.CompletedLocal || manifest.completedLocal || "",
    directoryPath,
    intervalMs: Number(manifest.IntervalMs ?? manifest.intervalMs) || 0,
    maxDurationSeconds: Number(manifest.MaxDurationSeconds ?? manifest.maxDurationSeconds) || 0,
    frameCount: frames.length,
    capturedFrameCount: frames.filter((frame) => frame.captured).length,
    failedFrameCount: frames.filter((frame) => !frame.captured).length,
    videoRequested: Boolean(manifest.VideoRequested ?? manifest.videoRequested),
    videoPath,
    videoUrl: videoPath ? pathToFileURL(videoPath).href : "",
    videoError: manifest.VideoError || manifest.videoError || "",
    totalMs: Number(manifest.TotalMs ?? manifest.totalMs) || 0,
    contextPath,
    context: contextPath ? safeReadText(contextPath, 80_000) : "",
    frames
  };
}

function summarizeLegacyFrames(directoryPath) {
  return scanFrameRecords(directoryPath).length;
}

function summarizeSession(root, directoryPath, manifest = {}) {
  const stats = fs.statSync(directoryPath);
  const id = manifest.Id || manifest.id || path.basename(directoryPath);
  const manifestFrameCount = Number(manifest.FrameCount ?? manifest.frameCount);
  const manifestCapturedCount = Number(manifest.CapturedFrameCount ?? manifest.capturedFrameCount);
  const manifestFailedCount = Number(manifest.FailedFrameCount ?? manifest.failedFrameCount);
  const hasManifestCounts = Number.isFinite(manifestFrameCount) ||
    Number.isFinite(manifestCapturedCount) ||
    Number.isFinite(manifestFailedCount);
  const legacyCapturedCount = hasManifestCounts ? 0 : summarizeLegacyFrames(directoryPath);
  const videoPath = existingSessionPath(
    root,
    directoryPath,
    manifest.VideoPath || manifest.videoPath,
    path.join(directoryPath, "video.mp4")
  );
  const startedUtc = manifest.StartedUtc || manifest.startedUtc || stats.birthtime.toISOString();

  return {
    id,
    status: manifest.Status || manifest.status || "incomplete",
    startedUtc,
    startedLocal: manifest.StartedLocal || manifest.startedLocal || startedUtc,
    completedUtc: manifest.CompletedUtc || manifest.completedUtc || "",
    completedLocal: manifest.CompletedLocal || manifest.completedLocal || "",
    directoryPath,
    intervalMs: Number(manifest.IntervalMs ?? manifest.intervalMs) || 0,
    maxDurationSeconds: Number(manifest.MaxDurationSeconds ?? manifest.maxDurationSeconds) || 0,
    frameCount: hasManifestCounts ? manifestFrameCount || 0 : legacyCapturedCount,
    capturedFrameCount: hasManifestCounts ? manifestCapturedCount || 0 : legacyCapturedCount,
    failedFrameCount: hasManifestCounts ? manifestFailedCount || 0 : 0,
    videoRequested: Boolean(manifest.VideoRequested ?? manifest.videoRequested),
    videoPath,
    videoUrl: videoPath ? pathToFileURL(videoPath).href : "",
    videoError: manifest.VideoError || manifest.videoError || "",
    totalMs: Number(manifest.TotalMs ?? manifest.totalMs) || 0
  };
}

function sessionDirectories(root) {
  if (!fs.existsSync(root)) {
    return [];
  }

  try {
    return fs.readdirSync(root, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => path.join(root, entry.name));
  } catch {
    return [];
  }
}

function listSessions(root, maxCount = 100) {
  const sessionRoot = path.resolve(root);
  const sessions = sessionDirectories(sessionRoot)
    .map((directoryPath) => {
      try {
        return { directoryPath, modifiedMs: fs.statSync(directoryPath).mtimeMs };
      } catch {
        return null;
      }
    })
    .filter(Boolean)
    .sort((a, b) => b.modifiedMs - a.modifiedMs)
    .slice(0, Math.max(1, Math.min(100, maxCount)))
    .map(({ directoryPath }) => {
      try {
        return summarizeSession(sessionRoot, directoryPath, safeReadJson(path.join(directoryPath, "session.json")) || {});
      } catch {
        return null;
      }
    })
    .filter(Boolean)
    .sort((a, b) => String(b.startedUtc).localeCompare(String(a.startedUtc)));

  return { root: sessionRoot, sessions };
}

function readSessionDetails(root, sessionId) {
  const sessionRoot = path.resolve(root);
  const directoryPath = sessionDirectories(sessionRoot).find((candidate) => {
    if (path.basename(candidate) === sessionId) {
      return true;
    }

    const manifest = safeReadJson(path.join(candidate, "session.json"));
    return (manifest?.Id || manifest?.id) === sessionId;
  });
  if (!sessionId || !directoryPath || !isUnderRoot(sessionRoot, directoryPath)) {
    throw new Error("Visual session was not found under the Winshots session root.");
  }

  return normalizeSession(sessionRoot, directoryPath, safeReadJson(path.join(directoryPath, "session.json")) || {});
}

module.exports = { listSessions, readSessionDetails };
