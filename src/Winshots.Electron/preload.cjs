const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("winshots", {
  listCaptures: () => ipcRenderer.invoke("captures:list"),
  readCaptureContext: (captureId) => ipcRenderer.invoke("captures:context", captureId),
  openCaptureFolder: (captureId) => ipcRenderer.invoke("captures:open-folder", captureId),
  copyScreenshotPath: (captureId) => ipcRenderer.invoke("captures:copy-path", captureId),
  copyScreenshotImage: (captureId) => ipcRenderer.invoke("captures:copy-image", captureId),
  copyCaptureContext: (captureId) => ipcRenderer.invoke("captures:copy-context", captureId),
  copyCaptureMetadata: (captureId) => ipcRenderer.invoke("captures:copy-metadata", captureId),
  trashCapture: (captureId) => ipcRenderer.invoke("captures:trash", captureId),
  minimize: () => ipcRenderer.invoke("window:minimize"),
  maximize: () => ipcRenderer.invoke("window:maximize"),
  close: () => ipcRenderer.invoke("window:close"),
  notifyReady: () => ipcRenderer.send("renderer:ready")
});
