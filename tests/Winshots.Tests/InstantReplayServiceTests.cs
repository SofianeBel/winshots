using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class InstantReplayServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Replay.Service.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryCompleteCycle_DoesNotStopNewerBufferFromOlderCompletion()
    {
        var oldBuffer = new InstantReplayBufferStore(Options("old"));
        var newBuffer = new InstantReplayBufferStore(Options("new"));
        newBuffer.SetRunning(true);
        bool stopCalled = false;

        bool completed = InstantReplayService.TryCompleteCycle(
            completedCycle: 1,
            currentCycle: 2,
            completedBuffer: oldBuffer,
            currentBuffer: newBuffer,
            markStopped: () =>
            {
                stopCalled = true;
                newBuffer.SetRunning(false);
            });

        Assert.False(completed);
        Assert.False(stopCalled);
        Assert.Equal("running", newBuffer.GetStatus(running: true).State);
    }

    private InstantReplayOptions Options(string name)
    {
        return new InstantReplayOptions
        {
            BufferRootPath = Path.Combine(_root, name, "buffer"),
            SessionRootPath = Path.Combine(_root, name, "sessions")
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
