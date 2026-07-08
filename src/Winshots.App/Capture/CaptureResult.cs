namespace Winshots.App.Capture;

public sealed record CaptureResult(
    CaptureMetadata Metadata,
    string DirectoryPath,
    string ScreenshotPath,
    string TextPath,
    string MetadataPath);
