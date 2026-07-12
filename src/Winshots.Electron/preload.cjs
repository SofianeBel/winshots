const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("winshots", {
  isSmoke: process.argv.includes("--winshots-smoke"),
  listCaptures: () => ipcRenderer.invoke("captures:list"),
  captureNow: (options) => ipcRenderer.invoke("app:capture", options),
  toggleTimeline: (options) => ipcRenderer.invoke("timeline:toggle", options),
  startVisualSession: (options) => ipcRenderer.invoke("sessions:start", options),
  stopVisualSession: () => ipcRenderer.invoke("sessions:stop"),
  getInstantReplayStatus: () => ipcRenderer.invoke("replay:status"),
  startInstantReplay: (options) => ipcRenderer.invoke("replay:start", options),
  stopInstantReplay: () => ipcRenderer.invoke("replay:stop"),
  saveInstantReplay: (options) => ipcRenderer.invoke("replay:save", options),
  listSessions: () => ipcRenderer.invoke("sessions:list"),
  readSessionDetails: (sessionId) => ipcRenderer.invoke("sessions:details", sessionId),
  readCaptureContext: (captureId) => ipcRenderer.invoke("captures:context", captureId),
  openCaptureFolder: (captureId) => ipcRenderer.invoke("captures:open-folder", captureId),
  copyScreenshotPath: (captureId) => ipcRenderer.invoke("captures:copy-path", captureId),
  copyScreenshotImage: (captureId) => ipcRenderer.invoke("captures:copy-image", captureId),
  copyCaptureContext: (captureId) => ipcRenderer.invoke("captures:copy-context", captureId),
  copyCaptureMetadata: (captureId) => ipcRenderer.invoke("captures:copy-metadata", captureId),
  trashCapture: (captureId) => ipcRenderer.invoke("captures:trash", captureId),
  packageInfo: () => ipcRenderer.invoke("package:info"),
  installPackage: () => ipcRenderer.invoke("package:install"),
  openPackageFolder: () => ipcRenderer.invoke("package:open-folder"),
  getStartupSettings: () => ipcRenderer.invoke("settings:startup:get"),
  setStartupEnabled: (enabled) => ipcRenderer.invoke("settings:startup:set", enabled),
  getBackgroundSettings: () => ipcRenderer.invoke("settings:background:get"),
  setBackgroundEnabled: (enabled) => ipcRenderer.invoke("settings:background:set", enabled),
  onCapturesChanged: (callback) => {
    const listener = () => callback();
    ipcRenderer.on("captures:changed", listener);
    return () => ipcRenderer.removeListener("captures:changed", listener);
  },
  minimize: () => ipcRenderer.invoke("window:minimize"),
  maximize: () => ipcRenderer.invoke("window:maximize"),
  close: () => ipcRenderer.invoke("window:close"),
  requestRepaint: () => ipcRenderer.send("window:invalidate"),
  notifyReady: () => ipcRenderer.send("renderer:ready")
});
