using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Winshots.App.Capture;

public static class PerceptualHash
{
    public static ulong Compute(string imagePath)
    {
        using Image image = Image.FromFile(imagePath);
        using var bitmap = new Bitmap(9, 8, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(image, 0, 0, bitmap.Width, bitmap.Height);
        }

        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width - 1; x++)
            {
                if (Luminance(bitmap.GetPixel(x, y)) > Luminance(bitmap.GetPixel(x + 1, y)))
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    public static int Distance(ulong left, ulong right)
    {
        return System.Numerics.BitOperations.PopCount(left ^ right);
    }

    private static int Luminance(Color color)
    {
        return (299 * color.R) + (587 * color.G) + (114 * color.B);
    }
}
