using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace Winshots.App.Capture;

public sealed class WindowsOcrTextExtractor
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<OcrTextExtractionResult> ExtractFileAsync(
        string imagePath,
        TimeSpan maxDuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var bitmap = new Bitmap(imagePath);
            return await ExtractBitmapAsync(bitmap, maxDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or IOException)
        {
            return new OcrTextExtractionResult(string.Empty, "failed", null, 0, 0, ex.Message);
        }
    }

    public async Task<OcrTextExtractionResult> ExtractBitmapAsync(
        Bitmap bitmap,
        TimeSpan maxDuration,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maxDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(3) : maxDuration);

        try
        {
            await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return CancelledOrTimedOut(stopwatch, cancellationToken, null, "OCR did not start within the available time budget.");
        }

        try
        {
            OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                stopwatch.Stop();
                return new OcrTextExtractionResult(
                    string.Empty,
                    "unavailable",
                    null,
                    stopwatch.ElapsedMilliseconds,
                    0,
                    "Windows has no OCR engine for the languages installed in the current user profile.");
            }

            using SoftwareBitmap softwareBitmap = CreateSoftwareBitmap(bitmap);
            Task<OcrResult> recognizeTask = engine.RecognizeAsync(softwareBitmap).AsTask(timeout.Token);
            try
            {
                OcrResult result = await recognizeTask.ConfigureAwait(false);
                string[] lines = result.Lines
                    .Select(static line => new
                    {
                        line.Text,
                        Top = line.Words.Count == 0 ? double.MaxValue : line.Words.Min(static word => word.BoundingRect.Y),
                        Left = line.Words.Count == 0 ? double.MaxValue : line.Words.Min(static word => word.BoundingRect.X)
                    })
                    .Where(static line => !string.IsNullOrWhiteSpace(line.Text))
                    .OrderBy(static line => line.Top)
                    .ThenBy(static line => line.Left)
                    .Select(static line => line.Text.Trim())
                    .ToArray();
                string text = string.Join(Environment.NewLine, lines);
                stopwatch.Stop();

                return new OcrTextExtractionResult(
                    text,
                    "succeeded",
                    engine.RecognizerLanguage.LanguageTag,
                    stopwatch.ElapsedMilliseconds,
                    lines.Length);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                return CancelledOrTimedOut(
                    stopwatch,
                    cancellationToken,
                    engine.RecognizerLanguage.LanguageTag,
                    "Windows OCR exceeded the available time budget.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new OcrTextExtractionResult(string.Empty, "failed", null, stopwatch.ElapsedMilliseconds, 0, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static OcrTextExtractionResult CancelledOrTimedOut(
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        string? language,
        string detail)
    {
        return new OcrTextExtractionResult(
            string.Empty,
            cancellationToken.IsCancellationRequested ? "cancelled" : "timed-out",
            language,
            stopwatch.ElapsedMilliseconds,
            0,
            detail);
    }

    private static SoftwareBitmap CreateSoftwareBitmap(Bitmap source)
    {
        using var bitmap = source.PixelFormat == PixelFormat.Format32bppArgb
            ? (Bitmap)source.Clone()
            : source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        Rectangle bounds = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = bitmap.Width * 4;
            byte[] pixels = new byte[rowBytes * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * rowBytes, rowBytes);
            }

            return SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(pixels),
                BitmapPixelFormat.Bgra8,
                bitmap.Width,
                bitmap.Height,
                BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
