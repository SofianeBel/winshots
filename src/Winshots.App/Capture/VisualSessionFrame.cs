namespace Winshots.App.Capture;

public sealed record VisualSessionFrame
{
    public required int Number { get; init; }
    public required string TimestampUtc { get; init; }
    public required string TimestampLocal { get; init; }
    public required bool Captured { get; init; }
    public string? Error { get; init; }
    public string? WindowTitle { get; init; }
    public string? ProcessName { get; init; }
    public string? ScreenshotPath { get; init; }
    public string? TextPath { get; init; }
    public string? MetadataPath { get; init; }
    public CaptureBounds? Bounds { get; init; }
    public CaptureMetrics? Metrics { get; init; }
    public CaptureDiagnostics? Diagnostics { get; init; }
    public string? PerceptualHash { get; init; }
    public string? RetentionReason { get; init; }
}
