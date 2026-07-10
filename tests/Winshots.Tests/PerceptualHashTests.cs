using System.Drawing.Imaging;
using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class PerceptualHashTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Hash.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Compute_ProducesStableHashAndDetectsOppositeGradient()
    {
        Directory.CreateDirectory(_root);
        string leftToRight = CreateGradient("left.png", reverse: false);
        string rightToLeft = CreateGradient("right.png", reverse: true);

        ulong first = PerceptualHash.Compute(leftToRight);
        ulong repeated = PerceptualHash.Compute(leftToRight);
        ulong opposite = PerceptualHash.Compute(rightToLeft);

        Assert.Equal(first, repeated);
        Assert.True(PerceptualHash.Distance(first, opposite) >= 8);
    }

    private string CreateGradient(string name, bool reverse)
    {
        string path = Path.Combine(_root, name);
        using var bitmap = new Bitmap(90, 80);
        for (int x = 0; x < bitmap.Width; x++)
        {
            int value = (int)Math.Round(255d * x / (bitmap.Width - 1));
            if (reverse)
            {
                value = 255 - value;
            }

            Color color = Color.FromArgb(value, value, value);
            for (int y = 0; y < bitmap.Height; y++)
            {
                bitmap.SetPixel(x, y, color);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
