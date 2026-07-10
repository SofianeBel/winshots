namespace Winshots.App.Capture;

public sealed record VisualSessionManifest
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required string StartedUtc { get; init; }
    public required string StartedLocal { get; init; }
    public string? CompletedUtc { get; init; }
    public string? CompletedLocal { get; init; }
    public required string DirectoryPath { get; init; }
    public required string FramesDirectoryPath { get; init; }
    public required string ContextsDirectoryPath { get; init; }
    public required string FramesIndexPath { get; init; }
    public required string ContextPath { get; init; }
    public required string ManifestPath { get; init; }
    public required int IntervalMs { get; init; }
    public required int MaxDurationSeconds { get; init; }
    public required bool VideoRequested { get; init; }
    public required int FrameCount { get; init; }
    public required int CapturedFrameCount { get; init; }
    public required int FailedFrameCount { get; init; }
    public string? VideoPath { get; init; }
    public string? VideoError { get; init; }
    public long? TotalMs { get; init; }
    public string? SessionType { get; init; }
    public string? Source { get; init; }
    public int? LookbackSeconds { get; init; }
    public string? WindowStartUtc { get; init; }
    public string? WindowEndUtc { get; init; }
    public int? DuplicateFrameCount { get; init; }
    public int? IgnoredFrameCount { get; init; }
    public int? ChangeEventCount { get; init; }
}
