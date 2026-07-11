using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class TextExtractionQualityTests
{
    [Fact]
    public void NeedsOcr_ReturnsTrue_ForSparseSteamLegacyTree()
    {
        var result = Result(
            "- fenêtre: Steam\n  - volet\n    - volet\n      - document: Chrome Legacy Window\n      - volet\n        - document",
            7);

        Assert.True(TextExtractionQuality.NeedsOcr(result));
    }

    [Fact]
    public void NeedsOcr_ReturnsFalse_ForRichAutomationTree()
    {
        var result = Result("- fenêtre: App\n  - bouton: Save\n  - texte: Document title\n  - texte: Ready", 12);

        Assert.False(TextExtractionQuality.NeedsOcr(result));
    }

    [Theory]
    [InlineData("failed", false)]
    [InlineData("timed-out", true)]
    public void NeedsOcr_ReturnsTrue_ForFailedOrTimedOutAutomation(string status, bool timedOut)
    {
        var result = Result("diagnostic", 0, status, timedOut);

        Assert.True(TextExtractionQuality.NeedsOcr(result));
    }

    [Fact]
    public void TextContext_KeepsAutomationAndOcrInSeparateArtifactSections()
    {
        var context = new TextContext(
            Result("- document: Chrome Legacy Window", 5),
            new OcrTextExtractionResult("Aimlabs\nRed Dead Redemption 2", "succeeded", "fr-FR", 25, 2));

        Assert.Contains("Chrome Legacy Window", context.ArtifactText);
        Assert.Contains("--- OCR context ---", context.ArtifactText);
        Assert.Contains("Red Dead Redemption 2", context.ArtifactText);
        Assert.Equal("windows_ui_automation+ocr", context.TextSource);
    }

    private static TextExtractionResult Result(
        string text,
        int nodes,
        string status = "succeeded",
        bool timedOut = false)
    {
        return new TextExtractionResult(text, nodes, false, false, timedOut, status);
    }
}
