using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Winshots.App.Capture;
using Winshots.App.Windows;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class WinshotsTools
{
    private static readonly ConcurrentDictionary<string, VisualSessionRecorder> Sessions = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [McpServerTool, Description("Capture the current Windows foreground window and return local screenshot/context artifact paths plus a text preview.")]
    public static string CaptureActiveWindow(
        [Description("Optional capture root. Defaults to the user's Documents\\Winshots\\captures folder.")] string? outputRoot = null,
        [Description("Delay before capture in milliseconds. Useful when the user needs time to focus another window.")] int delayMs = 250,
        [Description("Maximum number of context preview characters to include in the tool response.")] int maxPreviewCharacters = 6_000)
    {
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (!NativeMethods.IsUsableCaptureTarget(hwnd))
        {
            throw new InvalidOperationException("No usable foreground window is available to capture.");
        }

        var workflow = new CaptureWorkflow(ResolveRoot(outputRoot));
        CaptureResult result = workflow.CaptureWindow(hwnd, "mcp");
        string context = File.Exists(result.TextPath) ? File.ReadAllText(result.TextPath) : string.Empty;

        return JsonSerializer.Serialize(new
        {
            result.Metadata.Id,
            result.Metadata.TimestampLocal,
            result.Metadata.WindowTitle,
            result.Metadata.ProcessName,
            result.Metadata.Bounds,
            result.Metadata.Metrics,
            result.DirectoryPath,
            result.ScreenshotPath,
            result.TextPath,
            result.MetadataPath,
            ContextPreview = Truncate(context, Math.Clamp(maxPreviewCharacters, 1_000, 50_000))
        }, JsonOptions);
    }

    [McpServerTool, Description("List recent local Winshots captures.")]
    public static string ListRecentCaptures(
        [Description("Maximum number of captures to return.")] int maxCount = 10,
        [Description("Optional capture root. Defaults to the user's Documents\\Winshots\\captures folder.")] string? outputRoot = null)
    {
        var storage = new CaptureStorage(ResolveRoot(outputRoot));
        IReadOnlyList<CaptureResult> captures = storage.ListRecent(Math.Clamp(maxCount, 1, 50));

        return JsonSerializer.Serialize(captures.Select(static capture => new
        {
            capture.Metadata.Id,
            capture.Metadata.TimestampLocal,
            capture.Metadata.WindowTitle,
            capture.Metadata.ProcessName,
            capture.Metadata.Reason,
            capture.DirectoryPath,
            capture.ScreenshotPath,
            capture.TextPath,
            capture.MetadataPath,
            capture.Metadata.ExtractedTextLength,
            capture.Metadata.Metrics
        }), JsonOptions);
    }

    [McpServerTool, Description("Read the context text for a local Winshots capture by capture id or directory path.")]
    public static string ReadCaptureContext(
        [Description("Capture id returned by list_recent_captures, or a full capture directory path under the capture root.")] string captureIdOrDirectory,
        [Description("Optional capture root. Defaults to the user's Documents\\Winshots\\captures folder.")] string? outputRoot = null,
        [Description("Maximum number of context characters to return.")] int maxCharacters = 30_000)
    {
        string root = ResolveRoot(outputRoot);
        string directory = ResolveCaptureDirectory(root, captureIdOrDirectory);
        string contextPath = Path.Combine(directory, "context.txt");

        if (!File.Exists(contextPath))
        {
            throw new FileNotFoundException("Capture context was not found.", contextPath);
        }

        string context = File.ReadAllText(contextPath);
        return JsonSerializer.Serialize(new
        {
            DirectoryPath = directory,
            ContextPath = contextPath,
            Context = Truncate(context, Math.Clamp(maxCharacters, 1_000, 100_000))
        }, JsonOptions);
    }

    [McpServerTool, Description("Start a local visual debugging session that captures screenshot frames plus UI Automation context for Codex.")]
    public static string StartVisualSession(
        [Description("Optional session root. Defaults to the user's Documents\\Winshots\\sessions folder.")] string? outputRoot = null,
        [Description("Milliseconds between frame captures. Clamped between 250 and 60000.")] int intervalMs = 1000,
        [Description("Maximum session duration in seconds. Clamped between 1 and 3600.")] int maxDurationSeconds = 60,
        [Description("Try to create a video.mp4 with ffmpeg when the session completes.")] bool includeVideo = true,
        [Description("Delay before starting in milliseconds. Useful when the user needs time to focus another window.")] int delayMs = 250)
    {
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        var recorder = new VisualSessionRecorder(new VisualSessionOptions
        {
            RootPath = ResolveSessionRoot(outputRoot),
            IntervalMs = intervalMs,
            MaxDurationSeconds = maxDurationSeconds,
            IncludeVideo = includeVideo
        });

        if (!Sessions.TryAdd(recorder.Id, recorder))
        {
            throw new InvalidOperationException("A visual session with the same id already exists.");
        }

        recorder.Start(NativeMethods.GetForegroundWindow);

        return JsonSerializer.Serialize(new
        {
            recorder.Id,
            recorder.DirectoryPath,
            recorder.Manifest.FramesDirectoryPath,
            recorder.Manifest.ContextsDirectoryPath,
            recorder.Manifest.ContextPath,
            recorder.Manifest.IntervalMs,
            recorder.Manifest.MaxDurationSeconds,
            recorder.Manifest.VideoRequested
        }, JsonOptions);
    }

    [McpServerTool, Description("Stop a running local visual debugging session and finalize its context.md and optional video.mp4.")]
    public static string StopVisualSession(
        [Description("Session id returned by start_visual_session.")] string sessionId)
    {
        if (!Sessions.TryRemove(sessionId, out VisualSessionRecorder? recorder))
        {
            throw new InvalidOperationException("Visual session is not running in this MCP server process.");
        }

        VisualSessionManifest manifest = recorder.StopAsync().GetAwaiter().GetResult();
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    [McpServerTool, Description("List recent local Winshots visual debugging sessions.")]
    public static string ListVisualSessions(
        [Description("Maximum number of sessions to return.")] int maxCount = 10,
        [Description("Optional session root. Defaults to the user's Documents\\Winshots\\sessions folder.")] string? outputRoot = null)
    {
        var storage = new VisualSessionStorage(ResolveSessionRoot(outputRoot));
        return JsonSerializer.Serialize(storage.ListRecent(Math.Clamp(maxCount, 1, 50)), JsonOptions);
    }

    [McpServerTool, Description("Read the Markdown context for a local Winshots visual session by session id or directory path.")]
    public static string ReadVisualSessionContext(
        [Description("Session id returned by list_visual_sessions, or a full session directory path under the session root.")] string sessionIdOrDirectory,
        [Description("Optional session root. Defaults to the user's Documents\\Winshots\\sessions folder.")] string? outputRoot = null,
        [Description("Maximum number of context characters to return.")] int maxCharacters = 30_000)
    {
        var storage = new VisualSessionStorage(ResolveSessionRoot(outputRoot));
        VisualSessionManifest manifest = storage.ReadManifest(sessionIdOrDirectory);
        string context = File.Exists(manifest.ContextPath)
            ? File.ReadAllText(manifest.ContextPath)
            : VisualSessionStorage.BuildContextMarkdown(manifest, storage.ReadFrames(manifest));

        return JsonSerializer.Serialize(new
        {
            manifest.Id,
            manifest.DirectoryPath,
            manifest.ContextPath,
            manifest.VideoPath,
            Context = Truncate(context, Math.Clamp(maxCharacters, 1_000, 100_000))
        }, JsonOptions);
    }

    private static string ResolveRoot(string? outputRoot)
    {
        return string.IsNullOrWhiteSpace(outputRoot)
            ? CapturePaths.DefaultRoot
            : Path.GetFullPath(outputRoot);
    }

    private static string ResolveSessionRoot(string? outputRoot)
    {
        return string.IsNullOrWhiteSpace(outputRoot)
            ? CapturePaths.DefaultSessionRoot
            : Path.GetFullPath(outputRoot);
    }

    private static string ResolveCaptureDirectory(string root, string captureIdOrDirectory)
    {
        if (string.IsNullOrWhiteSpace(captureIdOrDirectory))
        {
            throw new ArgumentException("Capture id or directory is required.", nameof(captureIdOrDirectory));
        }

        string directory = Path.IsPathRooted(captureIdOrDirectory)
            ? Path.GetFullPath(captureIdOrDirectory)
            : Path.GetFullPath(Path.Combine(root, captureIdOrDirectory));

        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!normalizedDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Capture directory must stay under the Winshots capture root.");
        }

        return directory;
    }

    private static string Truncate(string value, int maxCharacters)
    {
        return value.Length <= maxCharacters ? value : value[..maxCharacters] + "\n[truncated]";
    }
}
