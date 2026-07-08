namespace Winshots.App.Capture;

public sealed record CaptureMetrics
{
    public required long TotalMs { get; init; }
    public required long ScreenshotMs { get; init; }
    public required long TextExtractionMs { get; init; }
    public long StorageWriteMs { get; init; }
    public required long ScreenshotBytes { get; init; }
    public required int AutomationNodeCount { get; init; }
    public required bool AutomationNodeLimitReached { get; init; }
    public required bool AutomationTextLimitReached { get; init; }
    public required bool AutomationTimedOut { get; init; }
}
