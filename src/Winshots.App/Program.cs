using System.Diagnostics;
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

        bool forceWinForms = args.Length > 0 && string.Equals(args[0], "--winforms", StringComparison.OrdinalIgnoreCase);
        if (forceWinForms)
        {
            args = args[1..];
        }
        else
        {
            if (TryRunElectronUi(args, out int electronExitCode))
            {
                Environment.ExitCode = electronExitCode;
                return;
            }

            ReportMissingElectronUi();
            Environment.ExitCode = 1;
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

    private static bool TryRunElectronUi(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!TryResolveElectronUi(out string electronExe, out string electronUi, out string workingDirectory))
        {
            return false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = electronExe;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add(electronUi);
            foreach (string arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            if (ShouldWaitForElectron(args))
            {
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ReportMissingElectronUi()
    {
        const string message =
            "The Winshots Electron UI was not found. Extract the full winshots-1.0.0-win-x64.zip package or run with --winforms to open the legacy fallback.";

        try
        {
            MessageBox.Show(message, "Winshots", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            Console.Error.WriteLine(message);
        }
    }

    private static bool ShouldWaitForElectron(string[] args)
    {
        return args.Any(static arg =>
            string.Equals(arg, "--smoke", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--screenshot", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveElectronUi(out string electronExe, out string electronUi, out string workingDirectory)
    {
        foreach ((string ElectronExe, string ElectronUi, string WorkingDirectory) candidate in ElectronCandidates())
        {
            if (File.Exists(candidate.ElectronExe) && IsElectronAppPath(candidate.ElectronUi))
            {
                electronExe = candidate.ElectronExe;
                electronUi = candidate.ElectronUi;
                workingDirectory = candidate.WorkingDirectory;
                return true;
            }
        }

        electronExe = string.Empty;
        electronUi = string.Empty;
        workingDirectory = string.Empty;
        return false;
    }

    private static bool IsElectronAppPath(string path)
    {
        return File.Exists(path) ||
            File.Exists(Path.Combine(path, "package.json")) ||
            File.Exists(Path.Combine(path, "index.js"));
    }

    private static IEnumerable<(string ElectronExe, string ElectronUi, string WorkingDirectory)> ElectronCandidates()
    {
        string? runtimeOverride = Environment.GetEnvironmentVariable("WINSHOTS_ELECTRON_RUNTIME");
        string? uiOverride = Environment.GetEnvironmentVariable("WINSHOTS_ELECTRON_UI");
        if (!string.IsNullOrWhiteSpace(runtimeOverride) && !string.IsNullOrWhiteSpace(uiOverride))
        {
            string electronExe = Path.Combine(Path.GetFullPath(runtimeOverride), "electron.exe");
            string electronUi = Path.GetFullPath(uiOverride);
            yield return (electronExe, electronUi, Path.GetDirectoryName(electronUi) ?? electronUi);
        }

        foreach (string packageRoot in PackageRootCandidates())
        {
            yield return (
                Path.Combine(packageRoot, "electron-runtime", "electron.exe"),
                Path.Combine(packageRoot, "electron-ui"),
                packageRoot);
        }

        foreach (string sourceRoot in SourceRootCandidates())
        {
            yield return (
                Path.Combine(sourceRoot, "node_modules", "electron", "dist", "electron.exe"),
                Path.Combine(sourceRoot, "src", "Winshots.Electron", "main.cjs"),
                sourceRoot);
        }
    }

    private static IEnumerable<string> PackageRootCandidates()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.GetParent(baseDirectory) is { } parent)
        {
            yield return parent.FullName;
        }

        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            yield break;
        }

        string exeDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(exeDirectory))
        {
            yield return exeDirectory;
            yield return Path.Combine(exeDirectory, Path.GetFileNameWithoutExtension(exePath));
        }
    }

    private static IEnumerable<string> SourceRootCandidates()
    {
        string? current = AppContext.BaseDirectory;
        for (int i = 0; !string.IsNullOrWhiteSpace(current) && i < 8; i++)
        {
            if (File.Exists(Path.Combine(current, "package.json")) &&
                File.Exists(Path.Combine(current, "src", "Winshots.Electron", "main.cjs")))
            {
                yield return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }
    }
}
