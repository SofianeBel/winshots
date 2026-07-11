using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Winshots.App.Windows;

namespace Winshots.App.Capture;

public static class WindowScreenshot
{
    private const uint WmPrint = 0x0317;
    private const uint PrintFlags = 0x0000003F;
    private const uint SendMessageTimeoutFlags = 0x00000001 | 0x00000002 | 0x00000020;
    private const uint WindowPrintTimeoutMs = 1500;
    private const int ErrorTimeout = 1460;
    private const long MaxPixelCount = 64_000_000;

    private static readonly CaptureStrategy[] Strategies =
    [
        new("wm-print", CaptureWithBoundedWindowPrint),
        new("copy-from-screen", CaptureFromVirtualScreen)
    ];

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    public static ScreenshotCaptureResult Save(IntPtr hwnd, string outputPath)
    {
        CaptureBounds bounds = NativeMethods.GetWindowBounds(hwnd);
        return Save(hwnd, outputPath, bounds, Strategies);
    }

    internal static Bitmap CaptureVisibleBitmap(IntPtr hwnd)
    {
        CaptureBounds bounds = NativeMethods.GetWindowBounds(hwnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The target window has no visible bounds.");
        }

        if ((long)bounds.Width * bounds.Height > MaxPixelCount)
        {
            throw new InvalidOperationException($"The window is too large to capture safely ({bounds.Width}x{bounds.Height}).");
        }

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            _ = CaptureFromVirtualScreen(hwnd, bounds, bitmap);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static ScreenshotCaptureResult Save(
        IntPtr hwnd,
        string outputPath,
        CaptureBounds bounds,
        IReadOnlyList<CaptureStrategy> strategies)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The foreground window has no visible bounds.");
        }

        if ((long)bounds.Width * bounds.Height > MaxPixelCount)
        {
            throw new InvalidOperationException($"The window is too large to capture safely ({bounds.Width}x{bounds.Height}).");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var attempts = new List<CaptureAttemptDiagnostics>();
        Bitmap? lastInvalidBitmap = null;
        string? lastInvalidStrategy = null;
        string? lastInvalidDetail = null;

        try
        {
            foreach (CaptureStrategy strategy in strategies)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                    string? strategyDetail = strategy.Capture(hwnd, bounds, bitmap);
                    ImageValidityAssessment assessment = AssessImage(bitmap);
                    stopwatch.Stop();

                    if (assessment.LikelyInvalid)
                    {
                        string detail = JoinDetails(strategyDetail, assessment.Detail);
                        attempts.Add(new CaptureAttemptDiagnostics
                        {
                            Strategy = strategy.Name,
                            Status = "invalid",
                            DurationMs = stopwatch.ElapsedMilliseconds,
                            Detail = detail
                        });
                        lastInvalidBitmap?.Dispose();
                        lastInvalidBitmap = (Bitmap)bitmap.Clone();
                        lastInvalidStrategy = strategy.Name;
                        lastInvalidDetail = detail;
                        continue;
                    }

                    attempts.Add(new CaptureAttemptDiagnostics
                    {
                        Strategy = strategy.Name,
                        Status = "succeeded",
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        Detail = strategyDetail
                    });
                    bitmap.Save(outputPath, ImageFormat.Png);

                    return new ScreenshotCaptureResult(
                        bounds,
                        new ImageCaptureDiagnostics
                        {
                            Status = attempts.Count == 1 ? "succeeded" : "fallback",
                            Strategy = strategy.Name,
                            Attempts = attempts,
                            LikelyInvalid = false,
                            Detail = strategyDetail
                        });
                }
                catch (CaptureStrategyException ex)
                {
                    stopwatch.Stop();
                    attempts.Add(new CaptureAttemptDiagnostics
                    {
                        Strategy = strategy.Name,
                        Status = ex.Status,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        Detail = ex.Message
                    });
                }
                catch (Exception ex) when (ex is ExternalException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    stopwatch.Stop();
                    attempts.Add(new CaptureAttemptDiagnostics
                    {
                        Strategy = strategy.Name,
                        Status = "failed",
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        Detail = ex.Message
                    });
                }
            }

            if (lastInvalidBitmap is not null)
            {
                lastInvalidBitmap.Save(outputPath, ImageFormat.Png);
                return new ScreenshotCaptureResult(
                    bounds,
                    new ImageCaptureDiagnostics
                    {
                        Status = "invalid",
                        Strategy = lastInvalidStrategy,
                        Attempts = attempts,
                        LikelyInvalid = true,
                        Detail = lastInvalidDetail
                    });
            }

            return new ScreenshotCaptureResult(
                bounds,
                new ImageCaptureDiagnostics
                {
                    Status = "failed",
                    Strategy = null,
                    Attempts = attempts,
                    LikelyInvalid = false,
                    Detail = attempts.LastOrDefault()?.Detail ?? "No capture strategy produced an image."
                });
        }
        finally
        {
            lastInvalidBitmap?.Dispose();
        }
    }

    public static ImageValidityAssessment AssessImage(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return new ImageValidityAssessment(true, "The captured image has no pixels.");
        }

        int stepX = Math.Max(1, bitmap.Width / 64);
        int stepY = Math.Max(1, bitmap.Height / 64);
        int samples = 0;
        int nearBlack = 0;

        for (int y = stepY / 2; y < bitmap.Height; y += stepY)
        {
            for (int x = stepX / 2; x < bitmap.Width; x += stepX)
            {
                Color pixel = bitmap.GetPixel(x, y);
                samples++;
                if (pixel.A <= 8 || (pixel.R <= 8 && pixel.G <= 8 && pixel.B <= 8))
                {
                    nearBlack++;
                }
            }
        }

        double nearBlackRatio = samples == 0 ? 1 : (double)nearBlack / samples;
        return nearBlackRatio >= 0.985
            ? new ImageValidityAssessment(true, $"The image is {nearBlackRatio:P0} black or transparent.")
            : new ImageValidityAssessment(false, null);
    }

    private static string? CaptureWithBoundedWindowPrint(IntPtr hwnd, CaptureBounds bounds, Bitmap bitmap)
    {
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        IntPtr hdc = graphics.GetHdc();
        try
        {
            Marshal.SetLastPInvokeError(0);
            IntPtr sendResult = SendMessageTimeout(
                hwnd,
                WmPrint,
                hdc,
                new IntPtr(PrintFlags),
                SendMessageTimeoutFlags,
                WindowPrintTimeoutMs,
                out _);
            if (sendResult == IntPtr.Zero)
            {
                int error = Marshal.GetLastPInvokeError();
                string status = error == ErrorTimeout ? "timed-out" : "failed";
                string detail = error switch
                {
                    ErrorTimeout => $"WM_PRINT timed out after {WindowPrintTimeoutMs}ms.",
                    0 => $"WM_PRINT failed or timed out within {WindowPrintTimeoutMs}ms.",
                    _ => $"WM_PRINT failed with Windows error {error}."
                };
                throw new CaptureStrategyException(status, detail);
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return $"WM_PRINT completed within the {WindowPrintTimeoutMs}ms limit.";
    }

    private static string? CaptureFromVirtualScreen(IntPtr hwnd, CaptureBounds bounds, Bitmap bitmap)
    {
        Rectangle window = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        Rectangle visible = Rectangle.Intersect(window, SystemInformation.VirtualScreen);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            throw new InvalidOperationException("The window does not intersect the Windows virtual screen.");
        }

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        graphics.CopyFromScreen(
            visible.Left,
            visible.Top,
            visible.Left - bounds.Left,
            visible.Top - bounds.Top,
            visible.Size,
            CopyPixelOperation.SourceCopy);

        return visible.Size == window.Size
            ? null
            : $"Captured the visible {visible.Width}x{visible.Height} area inside {bounds.Width}x{bounds.Height} window bounds.";
    }

    private static string JoinDetails(string? first, string? second)
    {
        return string.Join(" ", new[] { first, second }.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    internal sealed record CaptureStrategy(
        string Name,
        Func<IntPtr, CaptureBounds, Bitmap, string?> Capture);

    internal sealed class CaptureStrategyException(string status, string message) : Exception(message)
    {
        public string Status { get; } = status;
    }
}
