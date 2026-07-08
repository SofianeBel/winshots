using System.Drawing.Imaging;
using Winshots.App.Windows;

namespace Winshots.App.Capture;

public static class WindowScreenshot
{
    public static CaptureBounds Save(IntPtr hwnd, string outputPath)
    {
        CaptureBounds bounds = NativeMethods.GetWindowBounds(hwnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The foreground window has no visible bounds.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                new Size(bounds.Width, bounds.Height),
                CopyPixelOperation.SourceCopy);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
        return bounds;
    }
}
