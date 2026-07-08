using System.Globalization;
using System.Diagnostics;
using Winshots.App.Windows;

namespace Winshots.App.Capture;

public sealed class CaptureWorkflow
{
    private static readonly SemaphoreSlim CaptureGate = new(1, 1);

    private readonly CaptureStorage _storage;
    private readonly UiAutomationTextExtractor _textExtractor = new();

    public CaptureWorkflow(string rootPath)
    {
        _storage = new CaptureStorage(rootPath);
    }

    public CaptureStorage Storage => _storage;

    public CaptureResult CaptureWindow(IntPtr hwnd, string reason)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        WindowSnapshot window = NativeMethods.GetWindowSnapshot(hwnd);
        string directory = _storage.CreateCaptureDirectory(timestamp, window.Title);

        return CaptureWindowToPaths(
            hwnd,
            reason,
            directory,
            Path.Combine(directory, "screenshot.png"),
            Path.Combine(directory, "context.txt"),
            Path.Combine(directory, "metadata.json"),
            appendToIndex: true);
    }

    public CaptureResult CaptureWindowToPaths(
        IntPtr hwnd,
        string reason,
        string directory,
        string screenshotPath,
        string textPath,
        string metadataPath,
        bool appendToIndex,
        string? id = null,
        CaptureOptions? options = null)
    {
        CaptureGate.Wait();
        try
        {
            return CaptureWindowToPathsCore(hwnd, reason, directory, screenshotPath, textPath, metadataPath, appendToIndex, id, options);
        }
        finally
        {
            CaptureGate.Release();
        }
    }

    private CaptureResult CaptureWindowToPathsCore(
        IntPtr hwnd,
        string reason,
        string directory,
        string screenshotPath,
        string textPath,
        string metadataPath,
        bool appendToIndex,
        string? id,
        CaptureOptions? options)
    {
        if (!NativeMethods.IsUsableCaptureTarget(hwnd))
        {
            throw new InvalidOperationException("The selected window cannot be captured.");
        }

        options ??= CaptureOptions.Default;
        var totalStopwatch = Stopwatch.StartNew();
        DateTimeOffset timestamp = DateTimeOffset.Now;
        WindowSnapshot window = NativeMethods.GetWindowSnapshot(hwnd);

        var screenshotStopwatch = Stopwatch.StartNew();
        CaptureBounds bounds = WindowScreenshot.Save(hwnd, screenshotPath);
        screenshotStopwatch.Stop();

        long screenshotBytes = File.Exists(screenshotPath) ? new FileInfo(screenshotPath).Length : 0;

        var textStopwatch = Stopwatch.StartNew();
        TextExtractionResult extraction = _textExtractor.ExtractResult(hwnd, options.TextExtractionTimeout);
        textStopwatch.Stop();
        totalStopwatch.Stop();

        var metadata = new CaptureMetadata
        {
            Id = id ?? Path.GetFileName(directory),
            TimestampUtc = timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            TimestampLocal = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            Reason = reason,
            WindowTitle = window.Title,
            ProcessName = window.ProcessName,
            ProcessId = window.ProcessId,
            WindowHandle = $"0x{hwnd.ToInt64():X}",
            Bounds = bounds,
            ScreenshotPath = screenshotPath,
            TextPath = textPath,
            ExtractedTextLength = extraction.Text.Length,
            Metrics = new CaptureMetrics
            {
                TotalMs = totalStopwatch.ElapsedMilliseconds,
                ScreenshotMs = screenshotStopwatch.ElapsedMilliseconds,
                TextExtractionMs = textStopwatch.ElapsedMilliseconds,
                ScreenshotBytes = screenshotBytes,
                AutomationNodeCount = extraction.NodeCount,
                AutomationNodeLimitReached = extraction.NodeLimitReached,
                AutomationTextLimitReached = extraction.TextLimitReached,
                AutomationTimedOut = extraction.TimedOut
            }
        };

        return _storage.WriteCapture(directory, metadata, extraction.Text, metadataPath, appendToIndex);
    }
}
