using System.Diagnostics;
using Winshots.App.Windows;

namespace Winshots.App.Capture;

public sealed class InstantReplayService : IDisposable
{
    private readonly object _gate = new();
    private InstantReplayOptions _options;
    private InstantReplayBufferStore _buffer;
    private CancellationTokenSource? _stopSignal;
    private Task? _runTask;
    private long _cycle;
    private int _disposed;

    public InstantReplayService(InstantReplayOptions? options = null)
    {
        _options = (options ?? new InstantReplayOptions()).Normalized();
        _buffer = new InstantReplayBufferStore(_options);
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    public InstantReplayStatus Start(Func<IntPtr> selectTarget, InstantReplayOptions? options = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_runTask is { IsCompleted: false })
            {
                return _buffer.GetStatus(running: true);
            }

            if (options is not null)
            {
                _options = options.Normalized();
                _buffer = new InstantReplayBufferStore(_options);
            }

            _stopSignal?.Dispose();
            _stopSignal = new CancellationTokenSource();
            InstantReplayBufferStore cycleBuffer = _buffer;
            InstantReplayOptions cycleOptions = _options;
            CancellationTokenSource cycleStopSignal = _stopSignal;
            long cycle = ++_cycle;
            cycleBuffer.SetRunning(true);
            _runTask = Task.Run(() => RunAsync(selectTarget, cycleOptions, cycleBuffer, cycleStopSignal.Token, cycle));
            return cycleBuffer.GetStatus(running: true);
        }
    }

    public async Task<InstantReplayStatus> StopAsync()
    {
        Task? task;
        InstantReplayBufferStore buffer;
        long cycle;
        lock (_gate)
        {
            _stopSignal?.Cancel();
            task = _runTask;
            buffer = _buffer;
            cycle = _cycle;
        }

        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (cycle == _cycle && ReferenceEquals(buffer, _buffer))
            {
                return buffer.GetStatus(running: false);
            }

            return _buffer.GetStatus(_runTask is { IsCompleted: false });
        }
    }

    public InstantReplayStatus GetStatus()
    {
        lock (_gate)
        {
            return _buffer.GetStatus(_runTask is { IsCompleted: false });
        }
    }

    public VisualSessionManifest SaveReplay(int? lookbackSeconds = null)
    {
        InstantReplayBufferStore buffer;
        lock (_gate)
        {
            buffer = _buffer;
        }

        return buffer.SaveReplay(lookbackSeconds);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        _stopSignal?.Dispose();
    }

    private async Task RunAsync(
        Func<IntPtr> selectTarget,
        InstantReplayOptions options,
        InstantReplayBufferStore buffer,
        CancellationToken cancellationToken,
        long cycle)
    {
        var workflow = new CaptureWorkflow(options.BufferRootPath);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var sampleStopwatch = Stopwatch.StartNew();
                string? candidateDirectory = null;
                try
                {
                    IntPtr hwnd = selectTarget();
                    if (!NativeMethods.IsUsableCaptureTarget(hwnd))
                    {
                        buffer.RecordFailure("No usable foreground window was available for replay sampling.");
                    }
                    else
                    {
                        DateTimeOffset timestamp = DateTimeOffset.Now;
                        candidateDirectory = buffer.CreateCandidateDirectory(timestamp);
                        string id = Path.GetFileName(candidateDirectory);
                        ReplayCaptureAttempt attempt = workflow.TryCaptureReplayFrame(
                            hwnd,
                            candidateDirectory,
                            Path.Combine(candidateDirectory, "screenshot.png"),
                            Path.Combine(candidateDirectory, "context.txt"),
                            Path.Combine(candidateDirectory, "metadata.json"),
                            id,
                            options.ToCaptureOptions(),
                            TimeSpan.FromMilliseconds(options.CaptureGateWaitMs),
                            (window, hash) => buffer.DecideRetention(window, hash, timestamp));

                        if (attempt.Status == "retained" && attempt.Capture is not null && attempt.PerceptualHash is ulong hash)
                        {
                            buffer.AddRetainedFrame(attempt.Capture, hash, attempt.RetentionReason ?? "retained");
                            candidateDirectory = null;
                        }
                        else if (attempt.Status == "busy")
                        {
                            buffer.RecordBusySkip();
                        }
                        else if (attempt.Status == "failed")
                        {
                            buffer.RecordFailure(attempt.Error ?? "Replay capture failed.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    buffer.RecordFailure(ex.Message);
                }
                finally
                {
                    if (candidateDirectory is not null)
                    {
                        TryDeleteCandidate(candidateDirectory);
                    }
                }

                TimeSpan delay = TimeSpan.FromMilliseconds(options.IntervalMs) - sampleStopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                _ = TryCompleteCycle(
                    cycle,
                    _cycle,
                    buffer,
                    _buffer,
                    () => buffer.SetRunning(false));
            }
        }
    }

    internal static bool TryCompleteCycle(
        long completedCycle,
        long currentCycle,
        InstantReplayBufferStore completedBuffer,
        InstantReplayBufferStore currentBuffer,
        Action markStopped)
    {
        if (completedCycle != currentCycle || !ReferenceEquals(completedBuffer, currentBuffer))
        {
            return false;
        }

        markStopped();
        return true;
    }

    private static void TryDeleteCandidate(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Recovery removes an incomplete candidate after an interrupted sample.
        }
    }
}
