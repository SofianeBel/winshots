using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class InstantReplayCaptureGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Replay.Gate.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryCompleteReplayCapture_RemovesCandidateWhenSecondGateAcquisitionTimesOut()
    {
        Directory.CreateDirectory(_root);
        string screenshotPath = Path.Combine(_root, "candidate.png");
        File.WriteAllText(screenshotPath, "candidate");
        using var gate = new SemaphoreSlim(0, 1);
        var window = new WindowSnapshot(IntPtr.Zero, "Window", "process", 42, new CaptureBounds(0, 0, 100, 100));

        ReplayCaptureAttempt attempt = CaptureWorkflow.TryCompleteReplayCapture(
            gate,
            TimeSpan.FromMilliseconds(1),
            screenshotPath,
            window,
            0x1234UL,
            "visual-change",
            () => throw new InvalidOperationException("Completion must not run."));

        Assert.Equal("busy", attempt.Status);
        Assert.False(File.Exists(screenshotPath));
        Assert.Equal(0, gate.CurrentCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
