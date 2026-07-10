namespace Winshots.App.Capture;

public sealed record InstantReplayOptions
{
    public string BufferRootPath { get; init; } = CapturePaths.DefaultInstantReplayRoot;
    public string SessionRootPath { get; init; } = CapturePaths.DefaultSessionRoot;
    public int LookbackSeconds { get; init; } = 30;
    public int IntervalMs { get; init; } = 1000;
    public int MaxFrames { get; init; } = 480;
    public long MaxBytes { get; init; } = 256L * 1024 * 1024;
    public int HashDistanceThreshold { get; init; } = 8;
    public int StableKeyframeSeconds { get; init; } = 10;
    public int CaptureGateWaitMs { get; init; } = 50;
    public int TextExtractionTimeoutMs { get; init; } = 500;

    public InstantReplayOptions Normalized()
    {
        return this with
        {
            BufferRootPath = string.IsNullOrWhiteSpace(BufferRootPath) ? CapturePaths.DefaultInstantReplayRoot : Path.GetFullPath(BufferRootPath),
            SessionRootPath = string.IsNullOrWhiteSpace(SessionRootPath) ? CapturePaths.DefaultSessionRoot : Path.GetFullPath(SessionRootPath),
            LookbackSeconds = Math.Clamp(LookbackSeconds, 5, 120),
            IntervalMs = Math.Clamp(IntervalMs, 250, 5000),
            MaxFrames = Math.Clamp(MaxFrames, 2, 480),
            MaxBytes = Math.Clamp(MaxBytes, 32L * 1024 * 1024, 512L * 1024 * 1024),
            HashDistanceThreshold = Math.Clamp(HashDistanceThreshold, 1, 32),
            StableKeyframeSeconds = Math.Clamp(StableKeyframeSeconds, 2, 30),
            CaptureGateWaitMs = Math.Clamp(CaptureGateWaitMs, 0, 250),
            TextExtractionTimeoutMs = Math.Clamp(TextExtractionTimeoutMs, 100, 2000)
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
