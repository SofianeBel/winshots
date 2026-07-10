using System.Globalization;
using System.Text.Json;
using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class InstantReplayBufferStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Replay.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddRetainedFrame_PrunesToFrameAndLookbackBounds()
    {
        InstantReplayOptions options = Options(maxFrames: 2, lookbackSeconds: 5);
        var store = new InstantReplayBufferStore(options);
        DateTimeOffset now = DateTimeOffset.Now;

        store.AddRetainedFrame(CreateCapture(store, now.AddSeconds(-4), "one"), 1, "initial");
        store.AddRetainedFrame(CreateCapture(store, now.AddSeconds(-2), "two"), 2, "visual-change");
        store.AddRetainedFrame(CreateCapture(store, now, "three"), 3, "visual-change");

        InstantReplayStatus status = store.GetStatus(running: false);
        Assert.Equal(2, status.FrameCount);
        Assert.True(status.BufferBytes <= status.MaxBytes);
        Assert.False(Directory.Exists(Path.Combine(options.BufferRootPath, "frames", "one")));
    }

    [Fact]
    public void Recover_RemovesIncompleteFrameDirectoryAndKeepsCompleteFrame()
    {
        InstantReplayOptions options = Options();
        var firstStore = new InstantReplayBufferStore(options);
        firstStore.AddRetainedFrame(CreateCapture(firstStore, DateTimeOffset.Now, "complete"), 1, "initial");
        string incomplete = Path.Combine(options.BufferRootPath, "frames", "incomplete");
        Directory.CreateDirectory(incomplete);
        File.WriteAllText(Path.Combine(incomplete, "screenshot.png"), "partial");

        var recovered = new InstantReplayBufferStore(options);

        Assert.Equal(1, recovered.GetStatus(running: false).FrameCount);
        Assert.False(Directory.Exists(incomplete));
    }

    [Fact]
    public void Recover_RemovesFrameWithInvalidTimestampHashOrExternalPaths()
    {
        InstantReplayOptions options = Options();
        string directory = Path.Combine(options.BufferRootPath, "frames", "corrupt");
        Directory.CreateDirectory(directory);
        string screenshot = Path.Combine(directory, "screenshot.png");
        string context = Path.Combine(directory, "context.txt");
        string metadata = Path.Combine(directory, "metadata.json");
        File.WriteAllText(screenshot, "image");
        File.WriteAllText(context, "context");
        File.WriteAllText(metadata, "{}");
        File.WriteAllText(Path.Combine(directory, "frame.json"), JsonSerializer.Serialize(new
        {
            Id = "corrupt",
            TimestampUtc = "not-a-timestamp",
            TimestampLocal = "bad",
            WindowTitle = "Window",
            ProcessName = "app",
            ProcessId = 7,
            DirectoryPath = directory,
            ScreenshotPath = screenshot,
            TextPath = context,
            MetadataPath = metadata,
            PerceptualHash = "not-a-hash",
            RetentionReason = "initial",
            Bytes = 10
        }));

        var recovered = new InstantReplayBufferStore(options);

        Assert.Equal(0, recovered.GetStatus(running: false).FrameCount);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Recover_RejectsExternalFramePathWithoutDeletingExternalFile()
    {
        InstantReplayOptions options = Options();
        string directory = Path.Combine(options.BufferRootPath, "frames", "external");
        string external = Path.Combine(_root, "must-stay.png");
        Directory.CreateDirectory(directory);
        File.WriteAllText(external, "external");
        string context = Path.Combine(directory, "context.txt");
        string metadata = Path.Combine(directory, "metadata.json");
        File.WriteAllText(context, "context");
        File.WriteAllText(metadata, "{}");
        File.WriteAllText(Path.Combine(directory, "frame.json"), JsonSerializer.Serialize(new
        {
            Id = "external",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            TimestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            WindowTitle = "Window",
            ProcessName = "app",
            ProcessId = 7,
            DirectoryPath = directory,
            ScreenshotPath = external,
            TextPath = context,
            MetadataPath = metadata,
            PerceptualHash = "0000000000000001",
            RetentionReason = "initial",
            Bytes = 10
        }));

        var recovered = new InstantReplayBufferStore(options);

        Assert.Equal(0, recovered.GetStatus(running: false).FrameCount);
        Assert.False(Directory.Exists(directory));
        Assert.True(File.Exists(external));
    }

    [Fact]
    public void DecideRetention_ForcesWindowChangeAndCountsSameContextDuplicate()
    {
        InstantReplayOptions options = Options();
        var store = new InstantReplayBufferStore(options);
        DateTimeOffset now = DateTimeOffset.Now;
        store.AddRetainedFrame(CreateCapture(store, now, "first", "Window A", "app", 7), 0xAA, "initial");

        InstantReplayRetentionDecision duplicate = store.DecideRetention(
            new WindowSnapshot(IntPtr.Zero, "Window A", "app", 7, new CaptureBounds(0, 0, 10, 10)),
            0xAA,
            now.AddSeconds(1));
        InstantReplayRetentionDecision changed = store.DecideRetention(
            new WindowSnapshot(IntPtr.Zero, "Window B", "app", 7, new CaptureBounds(0, 0, 10, 10)),
            0xAA,
            now.AddSeconds(2));

        Assert.False(duplicate.Retain);
        Assert.True(changed.Retain);
        Assert.Equal("window-change", changed.Reason);
        Assert.Equal(1, store.GetStatus(running: false).DuplicateFrameCount);
    }

    [Fact]
    public void GetStatus_DoesNotRewriteStableBufferManifest()
    {
        InstantReplayOptions options = Options();
        var store = new InstantReplayBufferStore(options);
        store.AddRetainedFrame(CreateCapture(store, DateTimeOffset.Now, "stable"), 1, "initial");
        string manifestPath = Path.Combine(options.BufferRootPath, "buffer.json");
        DateTime before = File.GetLastWriteTimeUtc(manifestPath);
        Thread.Sleep(25);

        _ = store.GetStatus(running: false);
        _ = store.GetStatus(running: false);

        Assert.Equal(before, File.GetLastWriteTimeUtc(manifestPath));
    }

    [Fact]
    public void SaveReplay_PublishesAutonomousSessionsAndLeavesBufferReusable()
    {
        InstantReplayOptions options = Options();
        var store = new InstantReplayBufferStore(options);
        DateTimeOffset now = DateTimeOffset.Now;
        store.AddRetainedFrame(CreateCapture(store, now.AddSeconds(-1), "one"), 1, "initial");
        store.AddRetainedFrame(CreateCapture(store, now, "two"), 2, "visual-change");

        VisualSessionManifest first = store.SaveReplay();
        VisualSessionManifest second = store.SaveReplay();

        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        Assert.True(File.Exists(first.ManifestPath));
        Assert.True(File.Exists(second.ManifestPath));
        Assert.Equal("instant-replay", first.SessionType);
        Assert.Equal(2, new VisualSessionStorage(options.SessionRootPath).ReadFrames(first).Count);
        Assert.DoesNotContain(options.BufferRootPath, File.ReadAllText(first.ManifestPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(options.BufferRootPath, File.ReadAllText(first.FramesIndexPath), StringComparison.OrdinalIgnoreCase);
        Assert.All(Directory.EnumerateFiles(first.ContextsDirectoryPath, "*.metadata.json"), path =>
            Assert.DoesNotContain(options.BufferRootPath, File.ReadAllText(path), StringComparison.OrdinalIgnoreCase));
        Assert.All(Directory.EnumerateFiles(first.ContextsDirectoryPath, "*.txt"), path =>
            Assert.DoesNotContain(options.BufferRootPath, File.ReadAllText(path), StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateDirectories(options.SessionRootPath, "*.partial"));
        Assert.Equal(2, store.GetStatus(running: false).FrameCount);
    }

    [Fact]
    public async Task SaveReplay_SerializesConcurrentPublications()
    {
        InstantReplayOptions options = Options();
        var store = new InstantReplayBufferStore(options);
        store.AddRetainedFrame(CreateCapture(store, DateTimeOffset.Now, "one"), 1, "initial");

        VisualSessionManifest[] manifests = await Task.WhenAll(
            Task.Run(() => store.SaveReplay()),
            Task.Run(() => store.SaveReplay()));

        Assert.Equal(2, manifests.Select(static item => item.DirectoryPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(manifests, manifest => Assert.True(File.Exists(manifest.ManifestPath)));
        Assert.Empty(Directory.EnumerateDirectories(options.SessionRootPath, "*.partial"));
    }

    [Fact]
    public void SaveReplay_CleansPartialWhenSourceCopyFailsAndBufferRecovers()
    {
        InstantReplayOptions options = Options();
        var store = new InstantReplayBufferStore(options);
        CaptureResult broken = CreateCapture(store, DateTimeOffset.Now, "broken");
        store.AddRetainedFrame(broken, 1, "initial");
        File.Delete(broken.TextPath);

        Assert.Throws<FileNotFoundException>(() => store.SaveReplay());
        Assert.Empty(Directory.Exists(options.SessionRootPath)
            ? Directory.EnumerateDirectories(options.SessionRootPath, "*.partial")
            : []);

        Assert.Equal(0, store.GetStatus(running: false).FrameCount);
        store.AddRetainedFrame(CreateCapture(store, DateTimeOffset.Now, "replacement"), 2, "initial");
        VisualSessionManifest saved = store.SaveReplay();
        Assert.True(File.Exists(saved.ManifestPath));
    }

    [Fact]
    public void Recover_DeletesOnlyPartialOwnedByDeadProcess()
    {
        InstantReplayOptions options = Options();
        string orphan = Path.Combine(options.SessionRootPath, "old-instant-replay.dead.partial");
        string recentUnknown = Path.Combine(options.SessionRootPath, "recent-instant-replay.unknown.partial");
        Directory.CreateDirectory(orphan);
        Directory.CreateDirectory(recentUnknown);
        File.WriteAllText(Path.Combine(orphan, ".instant-replay-owner.json"), JsonSerializer.Serialize(new
        {
            ProcessId = int.MaxValue,
            ProcessStartedUtc = "2026-01-01T00:00:00.0000000Z",
            SaveStartedUtc = "2026-01-01T00:00:00.0000000Z"
        }));

        _ = new InstantReplayBufferStore(options);

        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(recentUnknown));
    }

    private InstantReplayOptions Options(int maxFrames = 10, int lookbackSeconds = 30)
    {
        return new InstantReplayOptions
        {
            BufferRootPath = Path.Combine(_root, "buffer"),
            SessionRootPath = Path.Combine(_root, "sessions"),
            MaxFrames = maxFrames,
            LookbackSeconds = lookbackSeconds,
            IntervalMs = 250,
            MaxBytes = 32L * 1024 * 1024
        }.Normalized();
    }

    private static CaptureResult CreateCapture(
        InstantReplayBufferStore store,
        DateTimeOffset timestamp,
        string id,
        string windowTitle = "Window",
        string processName = "app",
        int processId = 7)
    {
        string directory = Path.Combine(store.FramesRootPath, id);
        Directory.CreateDirectory(directory);
        string screenshot = Path.Combine(directory, "screenshot.png");
        string context = Path.Combine(directory, "context.txt");
        string metadataPath = Path.Combine(directory, "metadata.json");
        File.WriteAllBytes(screenshot, [1, 2, 3, 4]);
        File.WriteAllText(context, $"Screenshot: {screenshot}{Environment.NewLine}Context: {context}");
        var metadata = new CaptureMetadata
        {
            Id = id,
            TimestampUtc = timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            TimestampLocal = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            Reason = "instant-replay",
            WindowTitle = windowTitle,
            ProcessName = processName,
            ProcessId = processId,
            WindowHandle = "0x1",
            Bounds = new CaptureBounds(0, 0, 10, 10),
            ScreenshotPath = screenshot,
            TextPath = context,
            ExtractedTextLength = 4
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata));
        return new CaptureResult(metadata, directory, screenshot, context, metadataPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
