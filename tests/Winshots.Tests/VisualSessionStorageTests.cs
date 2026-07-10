using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class VisualSessionStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Session.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveSessionDirectory_RejectsPathsOutsideRoot()
    {
        var storage = new VisualSessionStorage(_root);

        Assert.Throws<InvalidOperationException>(() => storage.ResolveSessionDirectory(Path.GetTempPath()));
    }

    [Fact]
    public void ListRecent_ReturnsWrittenManifest()
    {
        var storage = new VisualSessionStorage(_root);
        string directory = storage.CreateSessionDirectory(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        var manifest = CreateManifest(directory);

        storage.WriteManifest(manifest);

        VisualSessionManifest result = Assert.Single(storage.ListRecent(10));
        Assert.Equal(manifest.Id, result.Id);
        Assert.Equal("running", result.Status);
    }

    [Fact]
    public void ListRecent_IgnoresPartialSessionWithManifest()
    {
        var storage = new VisualSessionStorage(_root);
        string directory = storage.CreateSessionDirectory(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        var manifest = CreateManifest(directory);
        string partialDirectory = directory + ".partial";
        Directory.Move(directory, partialDirectory);
        storage.WriteManifest(manifest with
        {
            DirectoryPath = partialDirectory,
            ManifestPath = Path.Combine(partialDirectory, "session.json")
        });

        Assert.Empty(storage.ListRecent(10));
    }

    [Fact]
    public void BuildContextMarkdown_IncludesFrameArtifacts()
    {
        var storage = new VisualSessionStorage(_root);
        string directory = storage.CreateSessionDirectory(DateTimeOffset.Now);
        var manifest = CreateManifest(directory);
        string contextPath = Path.Combine(directory, "contexts", "000001.txt");
        File.WriteAllText(contextPath, "button Save");

        var frame = new VisualSessionFrame
        {
            Number = 1,
            TimestampUtc = "2026-06-20T10:00:00.0000000Z",
            TimestampLocal = "2026-06-20 12:00:00 +02:00",
            Captured = true,
            WindowTitle = "Window",
            ProcessName = "notepad",
            ScreenshotPath = Path.Combine(directory, "frames", "000001.png"),
            TextPath = contextPath,
            MetadataPath = Path.Combine(directory, "contexts", "000001.metadata.json"),
            Bounds = new CaptureBounds(1, 2, 300, 200),
            Metrics = new CaptureMetrics
            {
                TotalMs = 10,
                ScreenshotMs = 2,
                TextExtractionMs = 5,
                ScreenshotBytes = 100,
                AutomationNodeCount = 3,
                AutomationNodeLimitReached = false,
                AutomationTextLimitReached = false,
                AutomationTimedOut = false
            }
        };

        string markdown = VisualSessionStorage.BuildContextMarkdown(manifest with { CapturedFrameCount = 1 }, [frame]);

        Assert.Contains("Frame 000001", markdown);
        Assert.Contains("button Save", markdown);
        Assert.Contains("total=10ms", markdown);
    }

    private static VisualSessionManifest CreateManifest(string directory)
    {
        return new VisualSessionManifest
        {
            Id = Path.GetFileName(directory),
            Status = "running",
            StartedUtc = "2026-06-20T10:00:00.0000000Z",
            StartedLocal = "2026-06-20 12:00:00 +02:00",
            DirectoryPath = directory,
            FramesDirectoryPath = Path.Combine(directory, "frames"),
            ContextsDirectoryPath = Path.Combine(directory, "contexts"),
            FramesIndexPath = Path.Combine(directory, "frames.jsonl"),
            ContextPath = Path.Combine(directory, "context.md"),
            ManifestPath = Path.Combine(directory, "session.json"),
            IntervalMs = 1000,
            MaxDurationSeconds = 60,
            VideoRequested = false,
            FrameCount = 0,
            CapturedFrameCount = 0,
            FailedFrameCount = 0
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
