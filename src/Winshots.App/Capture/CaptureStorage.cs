using System.Globalization;
using System.Text.Json;

namespace Winshots.App.Capture;

public sealed class CaptureStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public CaptureStorage(string rootPath)
    {
        RootPath = string.IsNullOrWhiteSpace(rootPath)
            ? throw new ArgumentException("Capture root is required.", nameof(rootPath))
            : Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public string CreateCaptureDirectory(DateTimeOffset timestamp, string windowTitle)
    {
        Directory.CreateDirectory(RootPath);

        string stamp = timestamp.LocalDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string safeTitle = SanitizeSegment(windowTitle);
        string basePath = Path.Combine(RootPath, $"{stamp}-{safeTitle}");

        for (int i = 1; ; i++)
        {
            string path = i == 1 ? basePath : $"{basePath}-{i}";
            string reservationPath = $"{path}.reserve";

            try
            {
                using var reservation = new FileStream(
                    reservationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);

                if (Directory.Exists(path))
                {
                    continue;
                }

                Directory.CreateDirectory(path);
                return path;
            }
            catch (IOException) when (File.Exists(reservationPath) || Directory.Exists(path))
            {
                // Another Winshots process reserved this capture name.
            }
            catch (UnauthorizedAccessException) when (File.Exists(reservationPath) || Directory.Exists(path))
            {
                // Another Winshots process reserved this capture name.
            }
        }
    }

    public CaptureResult WriteCapture(
        string directoryPath,
        CaptureMetadata metadata,
        string extractedText,
        string? metadataPath = null,
        bool appendToIndex = true)
    {
        Directory.CreateDirectory(directoryPath);

        metadataPath ??= Path.Combine(directoryPath, "metadata.json");
        string textPath = metadata.TextPath;
        Directory.CreateDirectory(Path.GetDirectoryName(metadata.TextPath) ?? directoryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath) ?? directoryPath);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        File.WriteAllText(textPath, BuildContextText(metadata, extractedText));
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
        stopwatch.Stop();

        CaptureMetadata finalMetadata = metadata.Metrics is null
            ? metadata
            : metadata with
            {
                Metrics = metadata.Metrics with
                {
                    StorageWriteMs = stopwatch.ElapsedMilliseconds,
                    TotalMs = metadata.Metrics.TotalMs + stopwatch.ElapsedMilliseconds
                }
            };

        if (!ReferenceEquals(finalMetadata, metadata))
        {
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(finalMetadata, JsonOptions));
        }

        if (appendToIndex)
        {
            File.AppendAllText(Path.Combine(RootPath, "index.jsonl"), JsonSerializer.Serialize(finalMetadata) + Environment.NewLine);
        }

        return new CaptureResult(finalMetadata, directoryPath, finalMetadata.ScreenshotPath, finalMetadata.TextPath, metadataPath);
    }

    public IReadOnlyList<CaptureResult> ListRecent(int maxCount)
    {
        if (!Directory.Exists(RootPath))
        {
            return [];
        }

        IReadOnlyList<CaptureResult> indexed = ListRecentFromIndex(maxCount);
        if (indexed.Count > 0)
        {
            return indexed;
        }

        return Directory
            .EnumerateFiles(RootPath, "metadata.json", SearchOption.AllDirectories)
            .Select(TryReadResult)
            .Where(static result => result is not null)
            .Cast<CaptureResult>()
            .OrderByDescending(static result => result.Metadata.TimestampUtc, StringComparer.Ordinal)
            .Take(maxCount)
            .ToArray();
    }

    public static string SanitizeSegment(string value, int maxLength = 72)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "untitled-window";
        }

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        char[] chars = value
            .Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '-' : ch)
            .ToArray();

        string collapsed = string.Join(
            "-",
            new string(chars).Split([' ', '\t', '\r', '\n', '-'], StringSplitOptions.RemoveEmptyEntries));

        string trimmed = collapsed.Trim('.', ' ', '-');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "untitled-window";
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].Trim('.', ' ', '-');
    }

    private static CaptureResult? TryReadResult(string metadataPath)
    {
        try
        {
            string json = File.ReadAllText(metadataPath);
            CaptureMetadata? metadata = JsonSerializer.Deserialize<CaptureMetadata>(json);
            if (metadata is null)
            {
                return null;
            }

            return new CaptureResult(
                metadata,
                Path.GetDirectoryName(metadataPath) ?? string.Empty,
                metadata.ScreenshotPath,
                metadata.TextPath,
                metadataPath);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<CaptureResult> ListRecentFromIndex(int maxCount)
    {
        string indexPath = Path.Combine(RootPath, "index.jsonl");
        if (!File.Exists(indexPath))
        {
            return [];
        }

        try
        {
            return File.ReadLines(indexPath)
                .Reverse()
                .Select(TryReadIndexedResult)
                .Where(static result => result is not null)
                .Cast<CaptureResult>()
                .Take(maxCount)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static CaptureResult? TryReadIndexedResult(string json)
    {
        try
        {
            CaptureMetadata? metadata = JsonSerializer.Deserialize<CaptureMetadata>(json);
            if (metadata is null)
            {
                return null;
            }

            string directory = Path.GetDirectoryName(metadata.ScreenshotPath) ?? string.Empty;
            return new CaptureResult(
                metadata,
                directory,
                metadata.ScreenshotPath,
                metadata.TextPath,
                Path.Combine(directory, "metadata.json"));
        }
        catch
        {
            return null;
        }
    }

    private static string BuildContextText(CaptureMetadata metadata, string extractedText)
    {
        string text = string.IsNullOrWhiteSpace(extractedText)
            ? "[No UI Automation text was exposed by this window.]"
            : extractedText.Trim();

        return $"""
               Winshots capture
               Timestamp: {metadata.TimestampLocal}
               Window: {metadata.WindowTitle}
               Process: {metadata.ProcessName} ({metadata.ProcessId})
               Reason: {metadata.Reason}
               Screenshot: {metadata.ScreenshotPath}
               Image capture: {metadata.Diagnostics?.Image.Status ?? "legacy"} via {metadata.Diagnostics?.Image.Strategy ?? "unknown"}
               UI Automation: {metadata.Diagnostics?.UiAutomation.Status ?? "legacy"}
               Metrics: total={metadata.Metrics?.TotalMs ?? 0}ms screenshot={metadata.Metrics?.ScreenshotMs ?? 0}ms text={metadata.Metrics?.TextExtractionMs ?? 0}ms storage={metadata.Metrics?.StorageWriteMs ?? 0}ms nodes={metadata.Metrics?.AutomationNodeCount ?? 0}

               --- UI Automation context ---
               {text}
               """;
    }
}
