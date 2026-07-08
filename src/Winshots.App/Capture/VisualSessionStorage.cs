using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshots.App.Capture;

public sealed class VisualSessionStorage
{
    private const int ContextPreviewCharacters = 6000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions LineJsonOptions = new();

    public VisualSessionStorage(string rootPath)
    {
        RootPath = string.IsNullOrWhiteSpace(rootPath)
            ? throw new ArgumentException("Session root is required.", nameof(rootPath))
            : Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public string CreateSessionDirectory(DateTimeOffset timestamp)
    {
        Directory.CreateDirectory(RootPath);

        string stamp = timestamp.LocalDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string basePath = Path.Combine(RootPath, $"{stamp}-visual-session");
        string path = basePath;

        for (int i = 2; Directory.Exists(path); i++)
        {
            path = $"{basePath}-{i}";
        }

        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "frames"));
        Directory.CreateDirectory(Path.Combine(path, "contexts"));
        return path;
    }

    public void WriteManifest(VisualSessionManifest manifest)
    {
        Directory.CreateDirectory(manifest.DirectoryPath);
        File.WriteAllText(manifest.ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public void AppendFrame(VisualSessionManifest manifest, VisualSessionFrame frame)
    {
        File.AppendAllText(manifest.FramesIndexPath, JsonSerializer.Serialize(frame, LineJsonOptions) + Environment.NewLine);
    }

    public IReadOnlyList<VisualSessionFrame> ReadFrames(VisualSessionManifest manifest)
    {
        if (!File.Exists(manifest.FramesIndexPath))
        {
            return [];
        }

        return File.ReadLines(manifest.FramesIndexPath)
            .Select(TryReadFrame)
            .Where(static frame => frame is not null)
            .Cast<VisualSessionFrame>()
            .ToArray();
    }

    public IReadOnlyList<VisualSessionManifest> ListRecent(int maxCount)
    {
        if (!Directory.Exists(RootPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(RootPath, "session.json", SearchOption.AllDirectories)
            .Select(TryReadManifest)
            .Where(static manifest => manifest is not null)
            .Cast<VisualSessionManifest>()
            .OrderByDescending(static manifest => manifest.StartedUtc, StringComparer.Ordinal)
            .Take(Math.Clamp(maxCount, 1, 50))
            .ToArray();
    }

    public string ResolveSessionDirectory(string sessionIdOrDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionIdOrDirectory))
        {
            throw new ArgumentException("Session id or directory is required.", nameof(sessionIdOrDirectory));
        }

        string directory = Path.IsPathRooted(sessionIdOrDirectory)
            ? Path.GetFullPath(sessionIdOrDirectory)
            : Path.GetFullPath(Path.Combine(RootPath, sessionIdOrDirectory));

        string normalizedRoot = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!normalizedDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Session directory must stay under the Winshots session root.");
        }

        return directory;
    }

    public VisualSessionManifest ReadManifest(string sessionIdOrDirectory)
    {
        string directory = ResolveSessionDirectory(sessionIdOrDirectory);
        string manifestPath = Path.Combine(directory, "session.json");
        VisualSessionManifest? manifest = TryReadManifest(manifestPath);
        return manifest ?? throw new FileNotFoundException("Visual session manifest was not found.", manifestPath);
    }

    public static string BuildContextMarkdown(VisualSessionManifest manifest, IReadOnlyList<VisualSessionFrame> frames)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Winshots visual session");
        builder.AppendLine();
        builder.AppendLine($"Session: {manifest.Id}");
        builder.AppendLine($"Started: {manifest.StartedLocal}");
        builder.AppendLine($"Completed: {manifest.CompletedLocal ?? "not completed"}");
        builder.AppendLine($"Frames: {manifest.CapturedFrameCount} captured, {manifest.FailedFrameCount} failed");
        builder.AppendLine($"Video: {(string.IsNullOrWhiteSpace(manifest.VideoPath) ? "not created" : manifest.VideoPath)}");
        builder.AppendLine();

        foreach (VisualSessionFrame frame in frames)
        {
            builder.AppendLine($"## Frame {frame.Number:000000}");
            builder.AppendLine();
            builder.AppendLine($"Time: {frame.TimestampLocal}");

            if (!frame.Captured)
            {
                builder.AppendLine($"Error: {frame.Error}");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine($"Window: {frame.WindowTitle}");
            builder.AppendLine($"Process: {frame.ProcessName}");
            builder.AppendLine($"Screenshot: {frame.ScreenshotPath}");
            builder.AppendLine($"Context: {frame.TextPath}");
            builder.AppendLine($"Metrics: total={frame.Metrics?.TotalMs ?? 0}ms screenshot={frame.Metrics?.ScreenshotMs ?? 0}ms text={frame.Metrics?.TextExtractionMs ?? 0}ms");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(ReadPreview(frame.TextPath));
            builder.AppendLine("```");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static VisualSessionManifest? TryReadManifest(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize<VisualSessionManifest>(File.ReadAllText(manifestPath));
        }
        catch
        {
            return null;
        }
    }

    private static VisualSessionFrame? TryReadFrame(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<VisualSessionFrame>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "[context file not found]";
        }

        string text = File.ReadAllText(path).Trim();
        return text.Length <= ContextPreviewCharacters
            ? text
            : text[..ContextPreviewCharacters] + Environment.NewLine + "[truncated]";
    }
}
