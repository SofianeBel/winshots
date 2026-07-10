using System.Text.Json;
using Winshots.App.Host;

namespace Winshots.Tests;

public sealed class HostEndpointRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Host.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Dispose_DoesNotDeleteDescriptorOwnedByNewerInstance()
    {
        string path = Path.Combine(_root, "host.json");
        using var older = new HostEndpointRegistry("older-pipe", path);
        using var newer = new HostEndpointRegistry("newer-pipe", path);

        older.Dispose();

        HostEndpointDescriptor current = HostEndpointRegistry.TryRead(path)!;
        Assert.Equal("newer-pipe", current.PipeName);
        newer.Dispose();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ReadLiveDescriptor_RejectsStaleProcess()
    {
        string path = Path.Combine(_root, "host.json");
        HostEndpointRegistry.WriteAtomic(path, new HostEndpointDescriptor
        {
            PipeName = "stale-pipe",
            ProcessId = int.MaxValue,
            ProcessStartedUtc = "2026-01-01T00:00:00.0000000Z",
            ProcessName = "Winshots.App",
            ExecutablePath = "C:\\missing\\Winshots.App.exe"
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => HostCommandClient.ReadLiveDescriptor(path));

        Assert.Contains("stale", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_RecoversAbandonedDescriptorMutex()
    {
        string path = Path.Combine(_root, "abandoned-host.json");
        string mutexName = HostEndpointRegistry.MutexName(Path.GetFullPath(path));
        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, mutexName);
            mutex.WaitOne();
        });
        thread.Start();
        thread.Join();

        using var registry = new HostEndpointRegistry("recovered-pipe", path);

        Assert.Equal("recovered-pipe", HostEndpointRegistry.TryRead(path)?.PipeName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
