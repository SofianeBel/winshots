using System.Text.Json;
using Winshots.App.Capture;

namespace Winshots.Tests;

public sealed class CaptureStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SanitizeSegment_ReplacesInvalidCharacters_AndKeepsNameShort()
    {
        string input = " A <bad>: window / title with a very very very very very very very very long suffix ";

        string sanitized = CaptureStorage.SanitizeSegment(input, maxLength: 24);

        Assert.StartsWith("A-bad-window-title", sanitized);
        Assert.DoesNotContain('<', sanitized);
        Assert.DoesNotContain(':', sanitized);
        Assert.True(sanitized.Length <= 24);
    }

    [Fact]
    public void CreateCaptureDirectory_CreatesUniqueDirectory_ForSameTimestampAndTitle()
    {
        var storage = new CaptureStorage(_root);
        DateTimeOffset timestamp = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

        string first = storage.CreateCaptureDirectory(timestamp, "Notepad");
        string second = storage.CreateCaptureDirectory(timestamp, "Notepad");

        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ListRecent_ReturnsMetadataWrittenToDisk()
    {
        var storage = new CaptureStorage(_root);
        string directory = storage.CreateCaptureDirectory(DateTimeOffset.Now, "Window");
        string screenshot = Path.Combine(directory, "screenshot.png");
        string context = Path.Combine(directory, "context.txt");
        File.WriteAllBytes(screenshot, [1, 2, 3]);

        var metadata = new CaptureMetadata
        {
            Id = Path.GetFileName(directory),
            TimestampUtc = "2026-06-20T10:00:00.0000000Z",
            TimestampLocal = "2026-06-20 12:00:00 +02:00",
            Reason = "test",
            WindowTitle = "Window",
            ProcessName = "notepad",
            ProcessId = 123,
            WindowHandle = "0x123",
            Bounds = new CaptureBounds(1, 2, 300, 200),
            ScreenshotPath = screenshot,
            TextPath = context,
            ExtractedTextLength = 5,
            Metrics = new CaptureMetrics
            {
                TotalMs = 12,
                ScreenshotMs = 3,
                TextExtractionMs = 7,
                StorageWriteMs = 0,
                ScreenshotBytes = 3,
                AutomationNodeCount = 4,
                AutomationNodeLimitReached = false,
                AutomationTextLimitReached = false,
                AutomationTimedOut = false
            }
        };

        storage.WriteCapture(directory, metadata, "hello");

        CaptureResult result = Assert.Single(storage.ListRecent(5));

        Assert.Equal("Window", result.Metadata.WindowTitle);
        Assert.True(File.Exists(result.MetadataPath));
        Assert.Contains("hello", File.ReadAllText(result.TextPath));
        Assert.Contains("total=", File.ReadAllText(result.TextPath));
        Assert.Contains("\"WindowTitle\": \"Window\"", File.ReadAllText(result.MetadataPath));
        CaptureMetadata? written = JsonSerializer.Deserialize<CaptureMetadata>(File.ReadAllText(result.MetadataPath));
        Assert.NotNull(written);
        Assert.True(written.Metrics?.StorageWriteMs >= 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
