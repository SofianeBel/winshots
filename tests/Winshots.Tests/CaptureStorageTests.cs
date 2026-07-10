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
    public void CreateCaptureDirectory_CreatesUniqueDirectories_WhenCalledConcurrently()
    {
        var storage = new CaptureStorage(_root);
        DateTimeOffset timestamp = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        var paths = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 20, _ => paths.Add(storage.CreateCaptureDirectory(timestamp, "Notepad")));

        Assert.Equal(20, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(paths, path => Assert.True(Directory.Exists(path)));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.reserve"));
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
            },
            Diagnostics = new CaptureDiagnostics
            {
                Image = new ImageCaptureDiagnostics
                {
                    Status = "fallback",
                    Strategy = "copy-from-screen",
                    Attempts =
                    [
                        new CaptureAttemptDiagnostics
                        {
                            Strategy = "wm-print",
                            Status = "invalid",
                            DurationMs = 2,
                            Detail = "black image"
                        },
                        new CaptureAttemptDiagnostics
                        {
                            Strategy = "copy-from-screen",
                            Status = "succeeded",
                            DurationMs = 1
                        }
                    ],
                    LikelyInvalid = false
                },
                UiAutomation = new UiAutomationDiagnostics
                {
                    Status = "succeeded"
                }
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
        Assert.Equal("fallback", written.Diagnostics?.Image.Status);
        Assert.Equal(2, written.Diagnostics?.Image.Attempts.Count);
    }

    [Fact]
    public void CaptureMetadata_DeserializesLegacyMetadataWithoutDiagnostics()
    {
        const string json = """
            {
              "Id": "legacy",
              "TimestampUtc": "2026-06-20T10:00:00.0000000Z",
              "TimestampLocal": "2026-06-20 12:00:00 +02:00",
              "Reason": "test",
              "WindowTitle": "Legacy window",
              "ProcessName": "notepad",
              "ProcessId": 123,
              "WindowHandle": "0x123",
              "Bounds": { "Left": 1, "Top": 2, "Width": 300, "Height": 200 },
              "ScreenshotPath": "C:\\captures\\screenshot.png",
              "TextPath": "C:\\captures\\context.txt",
              "ExtractedTextLength": 5
            }
            """;

        CaptureMetadata? metadata = JsonSerializer.Deserialize<CaptureMetadata>(json);

        Assert.NotNull(metadata);
        Assert.Equal("Legacy window", metadata.WindowTitle);
        Assert.Null(metadata.Diagnostics);
    }

    [Fact]
    public void CaptureResult_DoesNotExposeMissingFailedScreenshotAsAvailable()
    {
        var metadata = new CaptureMetadata
        {
            Id = "failed",
            TimestampUtc = "2026-07-10T10:00:00.0000000Z",
            TimestampLocal = "2026-07-10 12:00:00 +02:00",
            Reason = "test",
            WindowTitle = "Window",
            ProcessName = "test",
            ProcessId = 1,
            WindowHandle = "0x1",
            Bounds = new CaptureBounds(0, 0, 100, 100),
            ScreenshotPath = Path.Combine(_root, "missing.png"),
            TextPath = Path.Combine(_root, "context.txt"),
            ExtractedTextLength = 0,
            Diagnostics = new CaptureDiagnostics
            {
                Image = new ImageCaptureDiagnostics
                {
                    Status = "failed",
                    Attempts = [],
                    LikelyInvalid = false,
                    Detail = "All strategies failed."
                },
                UiAutomation = new UiAutomationDiagnostics { Status = "succeeded" }
            }
        };
        var result = new CaptureResult(metadata, _root, metadata.ScreenshotPath, metadata.TextPath, Path.Combine(_root, "metadata.json"));

        Assert.False(result.ImageCaptured);
        Assert.Equal("failed", result.ImageStatus);
        Assert.Null(result.AvailableScreenshotPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
