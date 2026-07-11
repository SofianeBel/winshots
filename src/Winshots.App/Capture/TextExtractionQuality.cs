namespace Winshots.App.Capture;

public static class TextExtractionQuality
{
    public static bool NeedsOcr(TextExtractionResult result)
    {
        if (result.Status is "failed" or "timed-out" || result.TimedOut)
        {
            return true;
        }

        string text = result.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        int meaningfulLines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(static line =>
                line.Contains(':', StringComparison.Ordinal) &&
                !line.TrimStart().StartsWith("- fenêtre:", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Chrome Legacy Window", StringComparison.OrdinalIgnoreCase) &&
                line.Any(char.IsLetterOrDigit));

        return result.NodeCount <= 10 && meaningfulLines < 2;
    }
}
