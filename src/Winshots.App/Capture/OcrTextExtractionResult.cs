namespace Winshots.App.Capture;

public sealed record OcrTextExtractionResult(
    string Text,
    string Status,
    string? Language,
    long DurationMs,
    int LineCount,
    string? Detail = null)
{
    public int CharacterCount => Text.Length;

    public static OcrTextExtractionResult NotNeeded { get; } =
        new(string.Empty, "not-needed", null, 0, 0);
}
