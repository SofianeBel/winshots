namespace Winshots.App.Capture;

public sealed record InstantReplayFrame
{
    public required string Id { get; init; }
    public required string TimestampUtc { get; init; }
    public required string TimestampLocal { get; init; }
    public required string WindowTitle { get; init; }
    public required string ProcessName { get; init; }
    public required int ProcessId { get; init; }
    public required string DirectoryPath { get; init; }
    public required string ScreenshotPath { get; init; }
    public required string TextPath { get; init; }
    public required string MetadataPath { get; init; }
    public required string PerceptualHash { get; init; }
    public required string RetentionReason { get; init; }
    public required long Bytes { get; init; }
}

public sealed record InstantReplayEvent
{
    public required string TimestampUtc { get; init; }
    public required string Type { get; init; }
    public required string Detail { get; init; }
}

public sealed record InstantReplayManifest
{
    public int Version { get; init; } = 1;
    public string Status { get; init; } = "stopped";
    public int LookbackSeconds { get; init; }
    public int IntervalMs { get; init; }
    public int MaxFrames { get; init; }
    public long MaxBytes { get; init; }
    public long BufferBytes { get; init; }
    public int DuplicateFrameCount { get; init; }
    public int FailedFrameCount { get; init; }
    public int BusySkipCount { get; init; }
    public int IgnoredFrameCount { get; init; }
    public int ChangeEventCount { get; init; }
    public IReadOnlyList<InstantReplayFrame> Frames { get; init; } = [];
    public IReadOnlyList<InstantReplayEvent> Events { get; init; } = [];
}

public sealed record InstantReplayRetentionDecision(bool Retain, string Reason, ulong PerceptualHash);

public sealed record ReplayCaptureAttempt(
    string Status,
    CaptureResult? Capture,
    WindowSnapshot? Window,
    ulong? PerceptualHash,
    string? RetentionReason,
    string? Error);

public sealed record InstantReplayStatus
{
    public required bool Running { get; init; }
    public required string State { get; init; }
    public required int LookbackSeconds { get; init; }
    public required int IntervalMs { get; init; }
    public required int FrameCount { get; init; }
    public required double BufferedSeconds { get; init; }
    public required long BufferBytes { get; init; }
    public required long MaxBytes { get; init; }
    public required int MaxFrames { get; init; }
    public required int DuplicateFrameCount { get; init; }
    public required int FailedFrameCount { get; init; }
    public required int BusySkipCount { get; init; }
    public required int IgnoredFrameCount { get; init; }
    public required int ChangeEventCount { get; init; }
}
