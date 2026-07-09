namespace Winshots.App.Capture;

public sealed record VisualSessionOptions
{
    public string RootPath { get; init; } = CapturePaths.DefaultSessionRoot;
    public int IntervalMs { get; init; } = 1000;
    public int MaxDurationSeconds { get; init; } = 60;
    public bool IncludeVideo { get; init; } = true;
    public int TextExtractionTimeoutMs { get; init; } = 1000;

    public VisualSessionOptions Normalized()
    {
        return this with
        {
            RootPath = string.IsNullOrWhiteSpace(RootPath) ? CapturePaths.DefaultSessionRoot : Path.GetFullPath(RootPath),
            IntervalMs = Math.Clamp(IntervalMs, 250, 60_000),
            MaxDurationSeconds = Math.Clamp(MaxDurationSeconds, 1, 3600),
            TextExtractionTimeoutMs = Math.Clamp(TextExtractionTimeoutMs, 100, 30_000)
        };
    }

    public CaptureOptions ToCaptureOptions()
    {
        return new CaptureOptions
        {
            TextExtractionTimeout = TimeSpan.FromMilliseconds(TextExtractionTimeoutMs)
        };
    }
}
