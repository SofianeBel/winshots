const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("winshots", {
  listCaptures: () => ipcRenderer.invoke("captures:list"),
  captureNow: (options) => ipcRenderer.invoke("app:capture", options),
  toggleTimeline: (options) => ipcRenderer.invoke("timeline:toggle", options),
  startVisualSession: (options) => ipcRenderer.invoke("sessions:start", options),
  stopVisualSession: () => ipcRenderer.invoke("sessions:stop"),
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
  minimize: () => ipcRenderer.invoke("window:minimize"),
  maximize: () => ipcRenderer.invoke("window:maximize"),
  close: () => ipcRenderer.invoke("window:close"),
  notifyReady: () => ipcRenderer.send("renderer:ready")
});
