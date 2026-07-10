const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { listSessions, readSessionDetails } = require("../src/Winshots.Electron/session-library.cjs");

function createRoot(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "winshots-session-library-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  return root;
}

test("lists a current session from manifest counts without reading frame details", (t) => {
  const root = createRoot(t);
  const directory = path.join(root, "current-session");
  fs.mkdirSync(path.join(directory, "frames"), { recursive: true });
  fs.mkdirSync(path.join(directory, "contexts"), { recursive: true });
  fs.writeFileSync(path.join(directory, "frames", "000001.png"), "image");
  fs.writeFileSync(path.join(directory, "contexts", "000001.txt"), "button Save");
  fs.writeFileSync(path.join(directory, "frames.jsonl"), `${JSON.stringify({ Number: 1, Captured: true })}\n`);
  fs.writeFileSync(path.join(directory, "session.json"), JSON.stringify({
    Id: "current-session",
    Status: "completed",
    StartedUtc: "2026-07-10T10:00:00.0000000Z",
    FrameCount: 3,
    CapturedFrameCount: 2,
    FailedFrameCount: 1
  }));

  const originalReadFileSync = fs.readFileSync;
  const detailReads = [];
  fs.readFileSync = function trackedRead(filePath, ...args) {
    if (/frames\.jsonl$|000001\.txt$|metadata\.json$/i.test(String(filePath))) {
      detailReads.push(String(filePath));
    }
    return originalReadFileSync.call(this, filePath, ...args);
  };

  let listed;
  try {
    listed = listSessions(root);
  } finally {
    fs.readFileSync = originalReadFileSync;
  }

  const details = readSessionDetails(root, "current-session");

  assert.equal(listed.sessions.length, 1);
  assert.equal(listed.sessions[0].capturedFrameCount, 2);
  assert.equal(listed.sessions[0].failedFrameCount, 1);
  assert.equal("frames" in listed.sessions[0], false);
  assert.equal("context" in listed.sessions[0], false);
  assert.deepEqual(detailReads, []);
  assert.equal(details.frames[0].context, "button Save");
  assert.match(details.frames[0].screenshotUrl, /^file:/);
});

test("lists a legacy session with a minimal frame directory scan", (t) => {
  const root = createRoot(t);
  const directory = path.join(root, "legacy-session");
  fs.mkdirSync(path.join(directory, "frames"), { recursive: true });
  fs.writeFileSync(path.join(directory, "frames", "000007.png"), "image");
  fs.writeFileSync(path.join(directory, "frames", "000008.png"), "image");

  const listed = listSessions(root);
  const details = readSessionDetails(root, "legacy-session");

  assert.equal(listed.sessions[0].capturedFrameCount, 2);
  assert.equal("frames" in listed.sessions[0], false);
  assert.equal("context" in listed.sessions[0], false);
  assert.equal(details.status, "incomplete");
  assert.equal(details.frames[0].number, 7);
  assert.equal(details.videoUrl, "");
  assert.equal(details.context, "");
});

test("limits summaries before opening session manifests", (t) => {
  const root = createRoot(t);
  for (let index = 1; index <= 3; index++) {
    const directory = path.join(root, `session-${index}`);
    fs.mkdirSync(directory, { recursive: true });
    fs.writeFileSync(path.join(directory, "session.json"), JSON.stringify({ Id: `session-${index}` }));
    const modified = new Date(2026, 6, 10, 10, index, 0);
    fs.utimesSync(directory, modified, modified);
  }

  const originalReadFileSync = fs.readFileSync;
  const manifestReads = [];
  fs.readFileSync = function trackedRead(filePath, ...args) {
    if (/session\.json$/i.test(String(filePath))) {
      manifestReads.push(String(filePath));
    }
    return originalReadFileSync.call(this, filePath, ...args);
  };

  let listed;
  try {
    listed = listSessions(root, 1);
  } finally {
    fs.readFileSync = originalReadFileSync;
  }

  assert.equal(listed.sessions.length, 1);
  assert.equal(listed.sessions[0].id, "session-3");
  assert.equal(manifestReads.length, 1);
});

test("ignores malformed frame lines and paths outside the session root", (t) => {
  const root = createRoot(t);
  const directory = path.join(root, "safe-session");
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(path.join(directory, "frames.jsonl"), [
    "not json",
    JSON.stringify({ Number: 1, Captured: true, ScreenshotPath: path.join(root, "..", "outside.png") })
  ].join("\n"));

  const details = readSessionDetails(root, "safe-session");

  assert.equal(details.frames.length, 1);
  assert.equal(details.frames[0].captured, false);
  assert.equal(details.frames[0].screenshotPath, "");
});

test("resolves a session whose legacy manifest id differs from its folder name", (t) => {
  const root = createRoot(t);
  const directory = path.join(root, "legacy-folder-name");
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(path.join(directory, "session.json"), JSON.stringify({ Id: "legacy-manifest-id" }));

  const listed = listSessions(root);
  const details = readSessionDetails(root, "legacy-manifest-id");

  assert.equal(listed.sessions[0].id, "legacy-manifest-id");
  assert.equal(details.id, "legacy-manifest-id");
});
