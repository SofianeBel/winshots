using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class WindowScreenshotTests
{
    [Fact]
    public void AssessImage_FlagsNearlyBlackImage()
    {
        using var bitmap = new Bitmap(64, 64);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
        }

        ImageValidityAssessment result = WindowScreenshot.AssessImage(bitmap);

        Assert.True(result.LikelyInvalid);
        Assert.Contains("black", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssessImage_AcceptsImageWithVisibleVariation()
    {
        using var bitmap = new Bitmap(64, 64);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            graphics.FillRectangle(Brushes.White, 0, 0, 32, 64);
        }

        ImageValidityAssessment result = WindowScreenshot.AssessImage(bitmap);

        Assert.False(result.LikelyInvalid);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void Save_RecordsTimeoutAndUsesFallbackStrategy()
    {
        string root = Path.Combine(Path.GetTempPath(), "Winshots.WindowScreenshot.Tests", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(root, "screenshot.png");
        var strategies = new WindowScreenshot.CaptureStrategy[]
        {
            new("wm-print", (_, _, _) => throw new WindowScreenshot.CaptureStrategyException("timed-out", "WM_PRINT timed out after 1500ms.")),
            new("copy-from-screen", (_, _, bitmap) =>
            {
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.White);
                graphics.FillRectangle(Brushes.Black, 0, 0, bitmap.Width / 2, bitmap.Height);
                return null;
            })
        };

        try
        {
            ScreenshotCaptureResult result = WindowScreenshot.Save(
                IntPtr.Zero,
                outputPath,
                new CaptureBounds(0, 0, 64, 64),
                strategies);

            Assert.Equal("fallback", result.Diagnostics.Status);
            Assert.Equal("copy-from-screen", result.Diagnostics.Strategy);
            Assert.Collection(
                result.Diagnostics.Attempts,
                attempt =>
                {
                    Assert.Equal("wm-print", attempt.Strategy);
                    Assert.Equal("timed-out", attempt.Status);
                },
                attempt =>
                {
                    Assert.Equal("copy-from-screen", attempt.Strategy);
                    Assert.Equal("succeeded", attempt.Status);
                });
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
