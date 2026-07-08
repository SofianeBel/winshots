using Winshots.App.Capture;
using Winshots.App.Codex;
using Winshots.App.UI;
using Winshots.App.Windows;

namespace Winshots.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && IsCaptureCommand(args[0]))
        {
            Environment.ExitCode = RunCaptureOnce(args[0], args[1..]);
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], "record-session", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = RunRecordSessionAsync(args[1..]).GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static bool IsCaptureCommand(string command)
    {
        return string.Equals(command, "capture-once", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "capture-to-codex", StringComparison.OrdinalIgnoreCase);
    }

    private static int RunCaptureOnce(string command, string[] args)
    {
        try
        {
            string outputRoot = CapturePaths.DefaultRoot;
            int delayMs = 0;
            string reason = string.Equals(command, "capture-to-codex", StringComparison.OrdinalIgnoreCase)
                ? "electron-codex"
                : "electron";
            bool pasteToCodex = string.Equals(command, "capture-to-codex", StringComparison.OrdinalIgnoreCase);
            bool json = false;
            var excludedProcessIds = new HashSet<int>();

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outputRoot = args[++i];
                }
                else if (string.Equals(args[i], "--delay-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    delayMs = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (string.Equals(args[i], "--reason", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    reason = args[++i];
                }
                else if (string.Equals(args[i], "--exclude-process-id", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    excludedProcessIds.Add(int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (string.Equals(args[i], "--json", StringComparison.OrdinalIgnoreCase))
                {
                    json = true;
                }
            }

            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }

            IntPtr hwnd = SelectCliCaptureTarget(excludedProcessIds);
            if (!NativeMethods.IsUsableCaptureTarget(hwnd))
            {
                throw new InvalidOperationException("No usable foreground window is available to capture.");
            }

            var workflow = new CaptureWorkflow(outputRoot);
            CaptureResult result = workflow.CaptureWindow(hwnd, reason);
            CodexPasteResult? paste = pasteToCodex ? CodexChatPaster.TryPasteCapture(result) : null;

            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    result.Metadata.Id,
                    result.DirectoryPath,
                    result.ScreenshotPath,
                    result.TextPath,
                    result.MetadataPath,
                    CodexPasteSuccess = paste?.Success,
                    CodexPasteMessage = paste?.Message
                }));
            }
            else
            {
                Console.WriteLine(result.DirectoryPath);
                if (paste is not null)
                {
                    Console.WriteLine(paste.Message);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunRecordSessionAsync(string[] args)
    {
        try
        {
            string outputRoot = CapturePaths.DefaultSessionRoot;
            int delayMs = 0;
            int durationSeconds = 5;
            int intervalMs = 1000;
            bool includeVideo = true;
            bool json = false;
            string? stopFile = null;
            var excludedProcessIds = new HashSet<int>();

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outputRoot = args[++i];
                }
                else if (string.Equals(args[i], "--delay-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    delayMs = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (string.Equals(args[i], "--duration-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    durationSeconds = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (string.Equals(args[i], "--interval-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    intervalMs = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (string.Equals(args[i], "--no-video", StringComparison.OrdinalIgnoreCase))
                {
                    includeVideo = false;
                }
                else if (string.Equals(args[i], "--stop-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    stopFile = args[++i];
                }
                else if (string.Equals(args[i], "--exclude-process-id", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    excludedProcessIds.Add(int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (string.Equals(args[i], "--json", StringComparison.OrdinalIgnoreCase))
                {
                    json = true;
                }
            }

            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
            }

            var recorder = new VisualSessionRecorder(new VisualSessionOptions
            {
                RootPath = outputRoot,
                IntervalMs = intervalMs,
                MaxDurationSeconds = durationSeconds,
                IncludeVideo = includeVideo
            });

            recorder.Start(() => SelectCliCaptureTarget(excludedProcessIds));
            VisualSessionManifest manifest;
            if (string.IsNullOrWhiteSpace(stopFile))
            {
                manifest = await recorder.WaitForCompletionAsync().ConfigureAwait(false);
            }
            else
            {
                manifest = await WaitForStopFileAsync(recorder, stopFile).ConfigureAwait(false);
            }

            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(manifest));
            }
            else
            {
                Console.WriteLine(manifest.DirectoryPath);
                if (!string.IsNullOrWhiteSpace(manifest.VideoPath))
                {
                    Console.WriteLine(manifest.VideoPath);
                }
                else if (!string.IsNullOrWhiteSpace(manifest.VideoError))
                {
                    Console.Error.WriteLine(manifest.VideoError);
                }
            }

            return manifest.CapturedFrameCount > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static IntPtr SelectCliCaptureTarget(HashSet<int> excludedProcessIds)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (NativeMethods.IsUsableCaptureTarget(foreground) &&
            !excludedProcessIds.Contains(NativeMethods.GetProcessId(foreground)))
        {
            return foreground;
        }

        return NativeMethods
            .EnumerateTopLevelWindows()
            .FirstOrDefault(hwnd =>
                NativeMethods.IsUsableCaptureTarget(hwnd) &&
                !excludedProcessIds.Contains(NativeMethods.GetProcessId(hwnd)));
    }

    private static async Task<VisualSessionManifest> WaitForStopFileAsync(VisualSessionRecorder recorder, string stopFile)
    {
        string fullStopFile = Path.GetFullPath(stopFile);
        while (recorder.IsRunning)
        {
            if (File.Exists(fullStopFile))
            {
                return await recorder.StopAsync().ConfigureAwait(false);
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        return await recorder.WaitForCompletionAsync().ConfigureAwait(false);
    }
}
