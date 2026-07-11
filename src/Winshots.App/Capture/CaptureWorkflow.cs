using System.Globalization;
using System.Diagnostics;
using Winshots.App.Windows;

namespace Winshots.App.Capture;

public sealed class CaptureWorkflow
{
    private static readonly SemaphoreSlim CaptureGate = new(1, 1);

    private readonly CaptureStorage _storage;
    private readonly UiAutomationTextExtractor _textExtractor = new();
    private readonly WindowsOcrTextExtractor _ocrExtractor = new();

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

    public ReplayCaptureAttempt TryCaptureReplayFrame(
        IntPtr hwnd,
        string directory,
        string screenshotPath,
        string textPath,
        string metadataPath,
        string id,
        CaptureOptions options,
        TimeSpan gateWait,
        Func<WindowSnapshot, ulong, InstantReplayRetentionDecision> decideRetention)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        WindowSnapshot? window = null;
        ScreenshotCaptureResult? screenshot = null;
        var totalStopwatch = Stopwatch.StartNew();
        var screenshotStopwatch = new Stopwatch();

        if (!CaptureGate.Wait(gateWait))
        {
            return new ReplayCaptureAttempt("busy", null, null, null, null, "Another capture owns the capture gate.");
        }

        try
        {
            if (!NativeMethods.IsUsableCaptureTarget(hwnd))
            {
                return new ReplayCaptureAttempt("failed", null, null, null, null, "The selected window cannot be captured.");
            }

            timestamp = DateTimeOffset.Now;
            window = NativeMethods.GetWindowSnapshot(hwnd);
            screenshotStopwatch.Start();
            screenshot = WindowScreenshot.Save(hwnd, screenshotPath);
            screenshotStopwatch.Stop();
        }
        catch (Exception ex)
        {
            return new ReplayCaptureAttempt("failed", null, window, null, null, ex.Message);
        }
        finally
        {
            CaptureGate.Release();
        }

        if (screenshot.Diagnostics.Status is "failed" or "invalid" || !File.Exists(screenshotPath))
        {
            TryDelete(screenshotPath);
            return new ReplayCaptureAttempt("failed", null, window, null, null, screenshot.Diagnostics.Detail ?? "No valid replay image was captured.");
        }

        ulong perceptualHash;
        InstantReplayRetentionDecision decision;
        try
        {
            perceptualHash = PerceptualHash.Compute(screenshotPath);
            decision = decideRetention(window!, perceptualHash);
        }
        catch (Exception ex)
        {
            TryDelete(screenshotPath);
            return new ReplayCaptureAttempt("failed", null, window, null, null, ex.Message);
        }

        if (!decision.Retain)
        {
            TryDelete(screenshotPath);
            return new ReplayCaptureAttempt("duplicate", null, window, perceptualHash, decision.Reason, null);
        }

        return TryCompleteReplayCapture(
            CaptureGate,
            gateWait,
            screenshotPath,
            window!,
            perceptualHash,
            decision.Reason,
            () => CompleteCapture(
                hwnd,
                "instant-replay",
                directory,
                screenshotPath,
                textPath,
                metadataPath,
                appendToIndex: false,
                id,
                options,
                timestamp,
                window!,
                screenshot,
                totalStopwatch,
                screenshotStopwatch.ElapsedMilliseconds));
    }

    internal static ReplayCaptureAttempt TryCompleteReplayCapture(
        SemaphoreSlim gate,
        TimeSpan gateWait,
        string screenshotPath,
        WindowSnapshot window,
        ulong perceptualHash,
        string retentionReason,
        Func<CaptureResult> completeCapture)
    {
        if (!gate.Wait(gateWait))
        {
            TryDelete(screenshotPath);
            return new ReplayCaptureAttempt("busy", null, window, perceptualHash, retentionReason, "Another capture owns the capture gate.");
        }

        try
        {
            CaptureResult result = completeCapture();
            return new ReplayCaptureAttempt("retained", result, window, perceptualHash, retentionReason, null);
        }
        catch (Exception ex)
        {
            TryDelete(screenshotPath);
            return new ReplayCaptureAttempt("failed", null, window, perceptualHash, retentionReason, ex.Message);
        }
        finally
        {
            gate.Release();
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
        ScreenshotCaptureResult screenshot = WindowScreenshot.Save(hwnd, screenshotPath);
        screenshotStopwatch.Stop();

        return CompleteCapture(
            hwnd,
            reason,
            directory,
            screenshotPath,
            textPath,
            metadataPath,
            appendToIndex,
            id,
            options,
            timestamp,
            window,
            screenshot,
            totalStopwatch,
            screenshotStopwatch.ElapsedMilliseconds);
    }

    private CaptureResult CompleteCapture(
        IntPtr hwnd,
        string reason,
        string directory,
        string screenshotPath,
        string textPath,
        string metadataPath,
        bool appendToIndex,
        string? id,
        CaptureOptions options,
        DateTimeOffset timestamp,
        WindowSnapshot window,
        ScreenshotCaptureResult screenshot,
        Stopwatch totalStopwatch,
        long screenshotMs)
    {
        long screenshotBytes = File.Exists(screenshotPath) ? new FileInfo(screenshotPath).Length : 0;

        var textStopwatch = Stopwatch.StartNew();
        TextExtractionResult extraction = _textExtractor.ExtractResult(hwnd, options.TextExtractionTimeout);
        textStopwatch.Stop();
        OcrTextExtractionResult ocr = OcrTextExtractionResult.NotNeeded;
        if (TextExtractionQuality.NeedsOcr(extraction) && File.Exists(screenshotPath))
        {
            ocr = _ocrExtractor
                .ExtractFileAsync(screenshotPath, options.OcrTimeout)
                .GetAwaiter()
                .GetResult();
        }
        var textContext = new TextContext(extraction, ocr);
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
            Bounds = screenshot.Bounds,
            ScreenshotPath = screenshotPath,
            TextPath = textPath,
            ExtractedTextLength = textContext.MatchText.Length,
            Metrics = new CaptureMetrics
            {
                TotalMs = totalStopwatch.ElapsedMilliseconds,
                ScreenshotMs = screenshotMs,
                TextExtractionMs = textStopwatch.ElapsedMilliseconds,
                OcrMs = ocr.DurationMs,
                ScreenshotBytes = screenshotBytes,
                AutomationNodeCount = extraction.NodeCount,
                AutomationNodeLimitReached = extraction.NodeLimitReached,
                AutomationTextLimitReached = extraction.TextLimitReached,
                AutomationTimedOut = extraction.TimedOut,
                OcrLineCount = ocr.LineCount,
                OcrCharacterCount = ocr.CharacterCount
            },
            Diagnostics = new CaptureDiagnostics
            {
                Image = screenshot.Diagnostics,
                UiAutomation = new UiAutomationDiagnostics
                {
                    Status = extraction.Status,
                    Detail = extraction.Detail
                },
                Ocr = ocr.Status == "not-needed" ? null : new OcrDiagnostics
                {
                    Status = ocr.Status,
                    Language = ocr.Language,
                    DurationMs = ocr.DurationMs,
                    LineCount = ocr.LineCount,
                    CharacterCount = ocr.CharacterCount,
                    Detail = ocr.Detail
                },
                TextSource = textContext.TextSource
            }
        };

        return _storage.WriteCapture(directory, metadata, textContext.ArtifactText, metadataPath, appendToIndex);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Recovery removes incomplete replay candidates on the next start.
        }
    }
}
