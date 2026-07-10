using System.Diagnostics;
using System.Globalization;
using Winshots.App.Windows;

namespace Winshots.App.Capture;

public sealed class VisualSessionRecorder
{
    private readonly object _gate = new();
    private readonly List<VisualSessionFrame> _frames = [];
    private readonly CancellationTokenSource _stopSignal = new();
    private readonly VisualSessionStorage _storage;
    private readonly CaptureWorkflow _workflow;
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly VisualSessionOptions _options;

    private Task? _runTask;
    private bool _finalized;
    private VisualSessionManifest _manifest;

    public VisualSessionRecorder(VisualSessionOptions options)
    {
        _options = options.Normalized();
        _storage = new VisualSessionStorage(_options.RootPath);
        _workflow = new CaptureWorkflow(_options.RootPath);

        DateTimeOffset now = DateTimeOffset.Now;
        string directory = _storage.CreateSessionDirectory(now);
        string id = Path.GetFileName(directory);

        _manifest = new VisualSessionManifest
        {
            Id = id,
            Status = "running",
            StartedUtc = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            StartedLocal = now.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            DirectoryPath = directory,
            FramesDirectoryPath = Path.Combine(directory, "frames"),
            ContextsDirectoryPath = Path.Combine(directory, "contexts"),
            FramesIndexPath = Path.Combine(directory, "frames.jsonl"),
            ContextPath = Path.Combine(directory, "context.md"),
            ManifestPath = Path.Combine(directory, "session.json"),
            IntervalMs = _options.IntervalMs,
            MaxDurationSeconds = _options.MaxDurationSeconds,
            VideoRequested = _options.IncludeVideo,
            FrameCount = 0,
            CapturedFrameCount = 0,
            FailedFrameCount = 0
        };

        _storage.WriteManifest(_manifest);
    }

    public string Id => _manifest.Id;
    public string DirectoryPath => _manifest.DirectoryPath;
    public VisualSessionManifest Manifest => _manifest;
    public bool IsRunning => _runTask is not null && !_runTask.IsCompleted;

    public void Start(Func<IntPtr> selectTarget)
    {
        if (_runTask is not null)
        {
            throw new InvalidOperationException("Visual session has already started.");
        }

        _sessionStopwatch.Start();
        _runTask = Task.Run(() => RunAsync(selectTarget, _stopSignal.Token));
    }

    public async Task<VisualSessionManifest> StopAsync()
    {
        _stopSignal.Cancel();

        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }

        return FinalizeSession();
    }

    public async Task<VisualSessionManifest> WaitForCompletionAsync()
    {
        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }

        return FinalizeSession();
    }

    private async Task RunAsync(Func<IntPtr> selectTarget, CancellationToken cancellationToken)
    {
        try
        {
            int frameNumber = 0;
            TimeSpan maxDuration = TimeSpan.FromSeconds(_options.MaxDurationSeconds);

            while (!cancellationToken.IsCancellationRequested && _sessionStopwatch.Elapsed < maxDuration)
            {
                var frameStopwatch = Stopwatch.StartNew();
                frameNumber++;
                AddFrame(CaptureFrame(frameNumber, selectTarget));

                TimeSpan delay = TimeSpan.FromMilliseconds(_options.IntervalMs) - frameStopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            FinalizeSession();
        }
    }

    private VisualSessionFrame CaptureFrame(int frameNumber, Func<IntPtr> selectTarget)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;

        try
        {
            IntPtr hwnd = selectTarget();
            if (!NativeMethods.IsUsableCaptureTarget(hwnd))
            {
                throw new InvalidOperationException("No usable foreground window is available to capture.");
            }

            string frameId = frameNumber.ToString("000000", CultureInfo.InvariantCulture);
            CaptureResult result = _workflow.CaptureWindowToPaths(
                hwnd,
                "visual-session",
                _manifest.DirectoryPath,
                Path.Combine(_manifest.FramesDirectoryPath, $"{frameId}.png"),
                Path.Combine(_manifest.ContextsDirectoryPath, $"{frameId}.txt"),
                Path.Combine(_manifest.ContextsDirectoryPath, $"{frameId}.metadata.json"),
                appendToIndex: false,
                id: $"{_manifest.Id}-{frameId}",
                options: _options.ToCaptureOptions());

            bool imageCaptured = result.ImageCaptured;
            return new VisualSessionFrame
            {
                Number = frameNumber,
                TimestampUtc = timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                TimestampLocal = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                Captured = imageCaptured,
                Error = imageCaptured ? null : result.Metadata.Diagnostics?.Image.Detail,
                WindowTitle = result.Metadata.WindowTitle,
                ProcessName = result.Metadata.ProcessName,
                ScreenshotPath = result.ScreenshotPath,
                TextPath = result.TextPath,
                MetadataPath = result.MetadataPath,
                Bounds = result.Metadata.Bounds,
                Metrics = result.Metadata.Metrics,
                Diagnostics = result.Metadata.Diagnostics
            };
        }
        catch (Exception ex)
        {
            return new VisualSessionFrame
            {
                Number = frameNumber,
                TimestampUtc = timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                TimestampLocal = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                Captured = false,
                Error = ex.Message
            };
        }
    }

    private void AddFrame(VisualSessionFrame frame)
    {
        lock (_gate)
        {
            _frames.Add(frame);
            _storage.AppendFrame(_manifest, frame);
            _manifest = _manifest with
            {
                FrameCount = _frames.Count,
                CapturedFrameCount = _frames.Count(static item => item.Captured),
                FailedFrameCount = _frames.Count(static item => !item.Captured)
            };
            _storage.WriteManifest(_manifest);
        }
    }

    private VisualSessionManifest FinalizeSession()
    {
        lock (_gate)
        {
            if (_finalized)
            {
                return _manifest;
            }

            _sessionStopwatch.Stop();

            string? videoPath = null;
            string? videoError = null;
            if (_options.IncludeVideo && _frames.Any(static frame => frame.Captured))
            {
                videoPath = Path.Combine(_manifest.DirectoryPath, "video.mp4");
                videoError = TryCreateVideo(videoPath);
                if (videoError is not null)
                {
                    videoPath = null;
                }
            }

            DateTimeOffset completed = DateTimeOffset.Now;
            _manifest = _manifest with
            {
                Status = "completed",
                CompletedUtc = completed.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                CompletedLocal = completed.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                FrameCount = _frames.Count,
                CapturedFrameCount = _frames.Count(static frame => frame.Captured),
                FailedFrameCount = _frames.Count(static frame => !frame.Captured),
                VideoPath = videoPath,
                VideoError = videoError,
                TotalMs = _sessionStopwatch.ElapsedMilliseconds
            };

            File.WriteAllText(_manifest.ContextPath, VisualSessionStorage.BuildContextMarkdown(_manifest, _frames));
            _storage.WriteManifest(_manifest);
            _finalized = true;
            return _manifest;
        }
    }

    private string? TryCreateVideo(string videoPath)
    {
        try
        {
            string listPath = Path.Combine(_manifest.DirectoryPath, "video-input.txt");
            double frameDurationSeconds = _options.IntervalMs / 1000.0;
            File.WriteAllText(listPath, BuildFfmpegConcatInput(frameDurationSeconds));

            using var process = new Process();
            process.StartInfo.FileName = "ffmpeg";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add("concat");
            process.StartInfo.ArgumentList.Add("-safe");
            process.StartInfo.ArgumentList.Add("0");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(listPath);
            process.StartInfo.ArgumentList.Add("-vf");
            process.StartInfo.ArgumentList.Add("scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:color=black");
            process.StartInfo.ArgumentList.Add("-c:v");
            process.StartInfo.ArgumentList.Add("libx264");
            process.StartInfo.ArgumentList.Add("-pix_fmt");
            process.StartInfo.ArgumentList.Add("yuv420p");
            process.StartInfo.ArgumentList.Add(videoPath);

            process.Start();
            string stderr = process.StandardError.ReadToEnd();
            _ = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                return "ffmpeg timed out.";
            }

            return process.ExitCode == 0 && File.Exists(videoPath)
                ? null
                : string.IsNullOrWhiteSpace(stderr) ? "ffmpeg failed." : stderr.Trim();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return ex.Message;
        }
    }

    private string BuildFfmpegConcatInput(double frameDurationSeconds)
    {
        var lines = new List<string>();
        VisualSessionFrame[] capturedFrames = _frames.Where(static frame => frame.Captured && File.Exists(frame.ScreenshotPath)).ToArray();

        foreach (VisualSessionFrame frame in capturedFrames)
        {
            lines.Add($"file '{NormalizeFfmpegPath(frame.ScreenshotPath!)}'");
            lines.Add($"duration {frameDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        if (capturedFrames.LastOrDefault() is { ScreenshotPath: { } lastPath })
        {
            lines.Add($"file '{NormalizeFfmpegPath(lastPath)}'");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string NormalizeFfmpegPath(string path)
    {
        return Path.GetFullPath(path).Replace("\\", "/").Replace("'", "'\\''", StringComparison.Ordinal);
    }
}
