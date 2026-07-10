namespace Winshots.App.Capture;

public sealed record CaptureResult(
    CaptureMetadata Metadata,
    string DirectoryPath,
    string ScreenshotPath,
    string TextPath,
    string MetadataPath)
{
    public string ImageStatus => Metadata.Diagnostics?.Image.Status ?? (File.Exists(ScreenshotPath) ? "legacy" : "missing");
    public bool ImageCaptured => File.Exists(ScreenshotPath) && ImageStatus is not ("failed" or "invalid" or "missing");
    public string? AvailableScreenshotPath => ImageCaptured ? ScreenshotPath : null;
}
