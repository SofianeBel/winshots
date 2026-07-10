namespace Winshots.App.Capture;

public sealed record CaptureDiagnostics
{
    public required ImageCaptureDiagnostics Image { get; init; }
    public required UiAutomationDiagnostics UiAutomation { get; init; }
}

public sealed record ImageCaptureDiagnostics
{
    public required string Status { get; init; }
    public string? Strategy { get; init; }
    public required IReadOnlyList<CaptureAttemptDiagnostics> Attempts { get; init; }
    public required bool LikelyInvalid { get; init; }
    public string? Detail { get; init; }
}

public sealed record CaptureAttemptDiagnostics
{
    public required string Strategy { get; init; }
    public required string Status { get; init; }
    public required long DurationMs { get; init; }
    public string? Detail { get; init; }
}

public sealed record UiAutomationDiagnostics
{
    public required string Status { get; init; }
    public string? Detail { get; init; }
}

public sealed record ScreenshotCaptureResult(
    CaptureBounds Bounds,
    ImageCaptureDiagnostics Diagnostics);

public sealed record ImageValidityAssessment(bool LikelyInvalid, string? Detail);
