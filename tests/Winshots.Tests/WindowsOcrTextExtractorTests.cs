using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class WindowsOcrTextExtractorTests
{
    [Fact]
    public async Task ExtractBitmapAsync_RecognizesFrenchAndEnglishLines()
    {
        using var bitmap = new Bitmap(1400, 420);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Arial", 64, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("BIBLIOTHÈQUE", font, Brushes.Black, 30, 25);
            graphics.DrawString("Aimlabs", font, Brushes.Black, 30, 145);
            graphics.DrawString("Red Dead Redemption 2", font, Brushes.Black, 30, 265);
        }

        var extractor = new WindowsOcrTextExtractor();
        OcrTextExtractionResult result = await extractor.ExtractBitmapAsync(bitmap, TimeSpan.FromSeconds(10));

        Assert.Equal("succeeded", result.Status);
        Assert.Contains("BIBLIOTH", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Aimlabs", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Red Dead Redemption 2", result.Text, StringComparison.OrdinalIgnoreCase);
    }
}
