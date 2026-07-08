namespace Winshots.App.Capture;

public sealed record WindowSnapshot(
    IntPtr Handle,
    string Title,
    string ProcessName,
    int ProcessId,
    CaptureBounds Bounds);
