namespace Winshots.App.Capture;

public sealed record CaptureOptions
{
    public static CaptureOptions Default { get; } = new();

    public TimeSpan TextExtractionTimeout { get; init; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan OcrTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
