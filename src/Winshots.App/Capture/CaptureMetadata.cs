namespace Winshots.App.Capture;

public sealed record CaptureMetadata
{
    public required string Id { get; init; }
    public required string TimestampUtc { get; init; }
    public required string TimestampLocal { get; init; }
    public required string Reason { get; init; }
    public required string WindowTitle { get; init; }
    public required string ProcessName { get; init; }
    public required int ProcessId { get; init; }
    public required string WindowHandle { get; init; }
    public required CaptureBounds Bounds { get; init; }
    public required string ScreenshotPath { get; init; }
    public required string TextPath { get; init; }
    public required int ExtractedTextLength { get; init; }
    public CaptureMetrics? Metrics { get; init; }
    public CaptureDiagnostics? Diagnostics { get; init; }
}
