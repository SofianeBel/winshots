using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Winshots.App.Capture;
using Winshots.App.Windows;
using Winshots.App.Host;

_ = NativeMethods.TryEnablePerMonitorV2DpiAwareness();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
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

    [McpServerTool, Description("List visible top-level Windows windows that Winshots can capture, optionally filtered by title or process name.")]
    public static string ListWindows(
        [Description("Optional case-insensitive title substring, for example YouTube, Twitter, Discord, or Steam.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring, with or without .exe.")] string? processName = null,
        [Description("Maximum number of windows to return.")] int maxCount = 25)
    {
        IReadOnlyList<WindowSnapshot> windows = FindCapturableWindows(titleContains, processName);
        IntPtr foreground = NativeMethods.GetForegroundWindow();

        return JsonSerializer.Serialize(windows.Take(Math.Clamp(maxCount, 1, 100)).Select(window => new
        {
            WindowHandle = FormatHandle(window.Handle),
            WindowTitle = window.Title,
            window.ProcessName,
            window.ProcessId,
            window.Bounds,
            IsForeground = window.Handle == foreground
        }), JsonOptions);
    }

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

        return SerializeCaptureResult(result, context, maxPreviewCharacters);
    }

    [McpServerTool, Description("Capture a specific visible Windows window selected by handle, title substring, or process name. Use list_windows first when possible.")]
    public static string CaptureWindow(
        [Description("Window handle returned by list_windows, for example 0x00123456.")] string? windowHandle = null,
        [Description("Optional case-insensitive title substring to select a window, for example YouTube or Steam.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring, with or without .exe.")] string? processName = null,
        [Description("Optional capture root. Defaults to the user's Documents\\Winshots\\captures folder.")] string? outputRoot = null,
        [Description("Delay before capture in milliseconds. Useful when waiting for a target page or app to settle.")] int delayMs = 250,
        [Description("Try to restore and foreground the target window before capturing it.")] bool activateWindow = true,
        [Description("Maximum number of context preview characters to include in the tool response.")] int maxPreviewCharacters = 6_000)
    {
        WindowSelection selection = ResolveWindowTarget(windowHandle, titleContains, processName);

        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        bool activationSucceeded = false;
        if (activateWindow)
        {
            activationSucceeded = TryActivateWindow(selection.Window.Handle);
            Thread.Sleep(150);
        }

        var workflow = new CaptureWorkflow(ResolveRoot(outputRoot));
        CaptureResult result = workflow.CaptureWindow(selection.Window.Handle, "mcp-targeted");
        string context = File.Exists(result.TextPath) ? File.ReadAllText(result.TextPath) : string.Empty;

        return SerializeCaptureResult(result, context, maxPreviewCharacters, new
        {
            RequestedWindowHandle = windowHandle,
            RequestedTitleContains = titleContains,
            RequestedProcessName = processName,
            SelectedWindowHandle = FormatHandle(selection.Window.Handle),
            SelectedWindowTitle = selection.Window.Title,
            selection.Window.ProcessName,
            selection.Window.ProcessId,
            selection.MatchCount,
            ActivationAttempted = activateWindow,
            ActivationSucceeded = activationSucceeded
        });
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
            ScreenshotPath = capture.AvailableScreenshotPath,
            ImageCaptured = capture.ImageCaptured,
            ImageStatus = capture.ImageStatus,
            capture.TextPath,
            capture.MetadataPath,
            capture.Metadata.ExtractedTextLength,
            capture.Metadata.Metrics,
            capture.Metadata.Diagnostics
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

    [McpServerTool, Description("Read status for the single Instant Replay buffer owned by the running Winshots host.")]
    public static string GetInstantReplayStatus()
    {
        return SendReplayHostCommand("replay.status");
    }

    [McpServerTool, Description("Start the single local Instant Replay buffer owned by the running Winshots host.")]
    public static string StartInstantReplay(
        [Description("Replay lookback in seconds. The host clamps this between 5 and 120.")] int lookbackSeconds = 30,
        [Description("Sampling interval in milliseconds. The host clamps this between 250 and 5000.")] int intervalMs = 1000)
    {
        return SendReplayHostCommand("replay.start", new Dictionary<string, object?>
        {
            ["lookbackSeconds"] = lookbackSeconds,
            ["intervalMs"] = intervalMs
        });
    }

    [McpServerTool, Description("Stop sampling for the single Instant Replay buffer owned by the running Winshots host. Retained frames stay local and can still be saved.")]
    public static string StopInstantReplay()
    {
        return SendReplayHostCommand("replay.stop");
    }

    [McpServerTool, Description("Save recent retained Instant Replay frames as an autonomous local Winshots session through the running host.")]
    public static string SaveInstantReplay(
        [Description("Optional number of recent seconds to save, bounded by the active host lookback.")] int? lookbackSeconds = null)
    {
        var payload = new Dictionary<string, object?>();
        if (lookbackSeconds is not null)
        {
            payload["lookbackSeconds"] = lookbackSeconds;
        }

        return SendReplayHostCommand("replay.save", payload, TimeSpan.FromSeconds(60));
    }

    [McpServerTool, Description("Wait until a matching capturable Windows window exists. Returns succeeded, timed_out, or cancelled with applied bounds and observation diagnostics.")]
    public static async Task<string> WaitForWindow(
        [Description("Optional window handle returned by list_windows.")] string? windowHandle = null,
        [Description("Optional case-insensitive window title substring.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring, with or without .exe.")] string? processName = null,
        [Description("Bounded wait timeout in milliseconds, clamped to 100-300000.")] int timeoutMs = 10_000,
        [Description("Polling cadence in milliseconds, clamped to 100-5000.")] int pollIntervalMs = 500,
        CancellationToken cancellationToken = default)
    {
        AgentWatchService service = CreateAgentWatchService(null);
        AgentWatchResult result = await service.WaitForWindowAsync(
            CreateAgentWatchTarget(windowHandle, titleContains, processName),
            CreateAgentWatchOptions(timeoutMs, pollIntervalMs),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool, Description("Wait until a case-insensitive text substring appears in the current local Windows UI Automation context for a matching window. No OCR is used.")]
    public static async Task<string> WaitForText(
        [Description("Required case-insensitive substring to find in Windows UI Automation text.")] string textContains,
        [Description("Optional window handle returned by list_windows.")] string? windowHandle = null,
        [Description("Optional case-insensitive window title substring.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring, with or without .exe.")] string? processName = null,
        [Description("Bounded wait timeout in milliseconds, clamped to 100-300000.")] int timeoutMs = 10_000,
        [Description("Polling cadence in milliseconds, clamped to 100-5000.")] int pollIntervalMs = 500,
        CancellationToken cancellationToken = default)
    {
        AgentWatchService service = CreateAgentWatchService(null);
        AgentWatchResult result = await service.WaitForTextAsync(
            CreateAgentWatchTarget(windowHandle, titleContains, processName),
            textContains,
            CreateAgentWatchOptions(timeoutMs, pollIntervalMs),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool, Description("Wait until a matching window changes by at least a deterministic perceptual dHash distance. Observation captures remain local.")]
    public static async Task<string> WaitForChange(
        [Description("Optional window handle returned by list_windows.")] string? windowHandle = null,
        [Description("Optional case-insensitive window title substring.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring, with or without .exe.")] string? processName = null,
        [Description("Optional capture root for local Agent Watch frame artifacts.")] string? outputRoot = null,
        [Description("Bounded wait timeout in milliseconds, clamped to 100-300000.")] int timeoutMs = 10_000,
        [Description("Polling cadence in milliseconds, clamped to 100-5000.")] int pollIntervalMs = 500,
        [Description("Minimum dHash Hamming distance treated as change, clamped to 1-64.")] int minHashDistance = 5,
        CancellationToken cancellationToken = default)
    {
        AgentWatchService service = CreateAgentWatchService(outputRoot);
        AgentWatchResult result = await service.WaitForChangeAsync(
            CreateAgentWatchTarget(windowHandle, titleContains, processName),
            CreateAgentWatchOptions(timeoutMs, pollIntervalMs) with { MinChangeHashDistance = minHashDistance },
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool, Description("Wait for a matching window, or optional UI Automation text within it, to disappear after it has first been observed. Initial absence does not succeed.")]
    public static async Task<string> WaitForDisappear(
        [Description("Optional case-insensitive Windows UI Automation text substring. Omit to wait for the window itself to disappear.")] string? textContains = null,
        [Description("Optional window handle returned by list_windows.")] string? windowHandle = null,
        [Description("Optional case-insensitive window title substring.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring, with or without .exe.")] string? processName = null,
        [Description("Bounded wait timeout in milliseconds, clamped to 100-300000.")] int timeoutMs = 10_000,
        [Description("Polling cadence in milliseconds, clamped to 100-5000.")] int pollIntervalMs = 500,
        CancellationToken cancellationToken = default)
    {
        AgentWatchService service = CreateAgentWatchService(null);
        AgentWatchResult result = await service.WaitForDisappearAsync(
            CreateAgentWatchTarget(windowHandle, titleContains, processName),
            textContains,
            CreateAgentWatchOptions(timeoutMs, pollIntervalMs),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool, Description("Wait until a matching window remains visually stable for an applied duration using bounded perceptual dHash comparisons. Observation captures remain local.")]
    public static async Task<string> WaitForStable(
        [Description("Optional window handle returned by list_windows.")] string? windowHandle = null,
        [Description("Optional case-insensitive window title substring.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring, with or without .exe.")] string? processName = null,
        [Description("Optional capture root for local Agent Watch frame artifacts.")] string? outputRoot = null,
        [Description("Bounded wait timeout in milliseconds, clamped to 100-300000.")] int timeoutMs = 10_000,
        [Description("Polling cadence in milliseconds, clamped to 100-5000.")] int pollIntervalMs = 500,
        [Description("Required stable duration in milliseconds, clamped to 100-60000.")] int stableDurationMs = 1_500,
        [Description("Maximum dHash Hamming distance still treated as stable, clamped to 0-64.")] int maxHashDistance = 2,
        CancellationToken cancellationToken = default)
    {
        AgentWatchService service = CreateAgentWatchService(outputRoot);
        AgentWatchResult result = await service.WaitForStableAsync(
            CreateAgentWatchTarget(windowHandle, titleContains, processName),
            CreateAgentWatchOptions(timeoutMs, pollIntervalMs) with
            {
                StableDurationMs = stableDurationMs,
                MaxStableHashDistance = maxHashDistance
            },
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool, Description("Start a local visual debugging session that captures screenshot frames plus UI Automation context for Codex.")]
    public static string StartVisualSession(
        [Description("Optional session root. Defaults to the user's Documents\\Winshots\\sessions folder.")] string? outputRoot = null,
        [Description("Milliseconds between frame captures. Clamped between 250 and 60000.")] int intervalMs = 1000,
        [Description("Maximum session duration in seconds. Clamped between 1 and 3600.")] int maxDurationSeconds = 60,
        [Description("Try to create a video.mp4 with ffmpeg when the session completes.")] bool includeVideo = true,
        [Description("Delay before starting in milliseconds. Useful when the user needs time to focus another window.")] int delayMs = 250,
        [Description("Optional window handle returned by list_windows. When omitted, the foreground window is captured each frame.")] string? windowHandle = null,
        [Description("Optional case-insensitive title substring for the visual session target.")] string? titleContains = null,
        [Description("Optional case-insensitive process name substring for the visual session target.")] string? processName = null,
        [Description("Try to restore and foreground the target window before the visual session starts.")] bool activateWindow = true)
    {
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }

        WindowSelection? selection = HasWindowSelector(windowHandle, titleContains, processName)
            ? ResolveWindowTarget(windowHandle, titleContains, processName)
            : null;

        bool activationSucceeded = false;
        if (selection is not null && activateWindow)
        {
            activationSucceeded = TryActivateWindow(selection.Window.Handle);
            Thread.Sleep(150);
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

        recorder.Start(selection is null
            ? NativeMethods.GetForegroundWindow
            : () => selection.Window.Handle);

        return JsonSerializer.Serialize(new
        {
            recorder.Id,
            recorder.DirectoryPath,
            recorder.Manifest.FramesDirectoryPath,
            recorder.Manifest.ContextsDirectoryPath,
            recorder.Manifest.ContextPath,
            recorder.Manifest.IntervalMs,
            recorder.Manifest.MaxDurationSeconds,
            recorder.Manifest.VideoRequested,
            Target = selection is null ? null : new
            {
                SelectedWindowHandle = FormatHandle(selection.Window.Handle),
                SelectedWindowTitle = selection.Window.Title,
                selection.Window.ProcessName,
                selection.Window.ProcessId,
                selection.MatchCount,
                ActivationAttempted = activateWindow,
                ActivationSucceeded = activationSucceeded
            }
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

    private static AgentWatchService CreateAgentWatchService(string? outputRoot)
    {
        return new AgentWatchService(ResolveRoot(outputRoot));
    }

    private static AgentWatchTarget CreateAgentWatchTarget(
        string? windowHandle,
        string? titleContains,
        string? processName)
    {
        return new AgentWatchTarget
        {
            WindowHandle = windowHandle,
            TitleContains = titleContains,
            ProcessName = processName
        };
    }

    private static AgentWatchOptions CreateAgentWatchOptions(int timeoutMs, int pollIntervalMs)
    {
        return new AgentWatchOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs
        };
    }

    private static string SendReplayHostCommand(
        string command,
        IReadOnlyDictionary<string, object?>? payload = null,
        TimeSpan? timeout = null)
    {
        JsonElement result = HostCommandClient.SendAsync(command, payload, timeout).GetAwaiter().GetResult();
        return result.GetRawText();
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

    private static IReadOnlyList<WindowSnapshot> FindCapturableWindows(string? titleContains, string? processName)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();

        return NativeMethods.EnumerateCapturableWindows()
            .Where(window => MatchesTitle(window, titleContains) && MatchesProcess(window, processName))
            .OrderByDescending(window => window.Handle == foreground)
            .ThenBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static WindowSelection ResolveWindowTarget(string? windowHandle, string? titleContains, string? processName)
    {
        if (!HasWindowSelector(windowHandle, titleContains, processName))
        {
            throw new ArgumentException("Provide windowHandle, titleContains, or processName to select a target window.");
        }

        if (!string.IsNullOrWhiteSpace(windowHandle))
        {
            IntPtr hwnd = ParseWindowHandle(windowHandle);
            if (!NativeMethods.IsUsableCaptureTarget(hwnd))
            {
                throw new InvalidOperationException($"Window handle {windowHandle} is not currently capturable.");
            }

            WindowSnapshot window = NativeMethods.GetWindowSnapshot(hwnd);
            if (!MatchesTitle(window, titleContains) || !MatchesProcess(window, processName))
            {
                throw new InvalidOperationException($"Window handle {windowHandle} does not match the supplied title/process filters.");
            }

            return new WindowSelection(window, 1);
        }

        IReadOnlyList<WindowSnapshot> matches = FindCapturableWindows(titleContains, processName);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException("No capturable window matched the supplied title/process filters.");
        }

        return new WindowSelection(matches[0], matches.Count);
    }

    private static bool HasWindowSelector(string? windowHandle, string? titleContains, string? processName)
    {
        return !string.IsNullOrWhiteSpace(windowHandle) ||
            !string.IsNullOrWhiteSpace(titleContains) ||
            !string.IsNullOrWhiteSpace(processName);
    }

    private static bool MatchesTitle(WindowSnapshot window, string? titleContains)
    {
        return string.IsNullOrWhiteSpace(titleContains) ||
            window.Title.Contains(titleContains.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesProcess(WindowSnapshot window, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return true;
        }

        string expected = Path.GetFileNameWithoutExtension(processName.Trim());
        return window.ProcessName.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
            window.ProcessName.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static IntPtr ParseWindowHandle(string windowHandle)
    {
        string value = windowHandle.Trim();
        long parsed = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

        return new IntPtr(parsed);
    }

    private static bool TryActivateWindow(IntPtr hwnd)
    {
        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        return NativeMethods.SetForegroundWindow(hwnd);
    }

    private static string SerializeCaptureResult(CaptureResult result, string context, int maxPreviewCharacters, object? target = null)
    {
        return JsonSerializer.Serialize(new
        {
            result.Metadata.Id,
            result.Metadata.TimestampLocal,
            result.Metadata.WindowTitle,
            result.Metadata.ProcessName,
            result.Metadata.Bounds,
            result.Metadata.Metrics,
            result.Metadata.Diagnostics,
            result.DirectoryPath,
            ScreenshotPath = result.AvailableScreenshotPath,
            ImageCaptured = result.ImageCaptured,
            ImageStatus = result.ImageStatus,
            result.TextPath,
            result.MetadataPath,
            Target = target,
            ContextPreview = Truncate(context, Math.Clamp(maxPreviewCharacters, 1_000, 50_000))
        }, JsonOptions);
    }

    private static string FormatHandle(IntPtr hwnd)
    {
        return $"0x{hwnd.ToInt64():X}";
    }

    private static string Truncate(string value, int maxCharacters)
    {
        return value.Length <= maxCharacters ? value : value[..maxCharacters] + "\n[truncated]";
    }

    private sealed record WindowSelection(WindowSnapshot Window, int MatchCount);
}
