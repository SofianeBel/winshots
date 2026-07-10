using System.Diagnostics;
using Winshots.App.Host;
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
        ApplicationConfiguration.Initialize();

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
            Environment.ExitCode = RunElectronHost(args);
            return;
        }

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
            bool imageCaptured = result.ImageCaptured;
            CodexPasteResult? paste = pasteToCodex && imageCaptured ? CodexChatPaster.TryPasteCapture(result) : null;

            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    result.Metadata.Id,
                    result.DirectoryPath,
                    ScreenshotPath = result.AvailableScreenshotPath,
                    ImageCaptured = result.ImageCaptured,
                    ImageStatus = result.ImageStatus,
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

            if (!imageCaptured)
            {
                Console.Error.WriteLine($"Capture image {result.ImageStatus}: {result.Metadata.Diagnostics?.Image.Detail}");
            }

            return imageCaptured ? 0 : 1;
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

    private static int RunElectronHost(string[] args)
    {
        using var mainForm = new MainForm();
        _ = mainForm.Handle;

        using var commandServer = new HostCommandServer(mainForm);
        commandServer.Start();

        if (!TryStartElectronUi(args, commandServer.PipeName, mainForm, out Process? electronProcess))
        {
            ReportMissingElectronUi();
            return 1;
        }

        using var context = new ElectronHostApplicationContext(mainForm, electronProcess!, commandServer);
        Application.Run(context);
        return context.ExitCode;
    }

    private static bool TryStartElectronUi(string[] args, string? hostPipeName, MainForm? hostForm, out Process? process)
    {
        process = null;
        if (!TryResolveElectronUi(out string electronExe, out string electronUi, out string workingDirectory))
        {
            return false;
        }

        try
        {
            var electronProcess = new Process();
            electronProcess.StartInfo.FileName = electronExe;
            electronProcess.StartInfo.WorkingDirectory = workingDirectory;
            electronProcess.StartInfo.UseShellExecute = false;
            electronProcess.StartInfo.CreateNoWindow = true;
            electronProcess.StartInfo.ArgumentList.Add(electronUi);
            foreach (string arg in args)
            {
                electronProcess.StartInfo.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrWhiteSpace(hostPipeName))
            {
                electronProcess.StartInfo.Environment["WINSHOTS_HOST_PIPE"] = hostPipeName;
            }

            hostForm?.ExcludeProcessPathPrefix(Path.GetDirectoryName(electronExe) ?? string.Empty);
            hostForm?.PrimeCaptureTarget();

            electronProcess.Start();
            hostForm?.ExcludeProcess(electronProcess.Id);
            process = electronProcess;

            return true;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    private static void ReportMissingElectronUi()
    {
        const string message =
            "The Winshots Electron UI was not found. Extract the full winshots-1.1.0-win-x64.zip package or run with --winforms to open the legacy fallback.";

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

    private sealed class ElectronHostApplicationContext : ApplicationContext
    {
        private readonly MainForm _mainForm;
        private readonly Process _electronProcess;
        private readonly HostCommandServer _commandServer;
        private bool _exiting;

        public ElectronHostApplicationContext(MainForm mainForm, Process electronProcess, HostCommandServer commandServer)
        {
            _mainForm = mainForm;
            _electronProcess = electronProcess;
            _commandServer = commandServer;

            _electronProcess.EnableRaisingEvents = true;
            _electronProcess.Exited += (_, _) =>
            {
                ExitCode = _electronProcess.ExitCode;
                ExitFromAnyThread();
            };
            _mainForm.FormClosed += (_, _) => ExitFromAnyThread();
        }

        public int ExitCode { get; private set; }

        protected override void ExitThreadCore()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            _commandServer.Dispose();

            try
            {
                if (!_electronProcess.HasExited)
                {
                    _electronProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The Electron process may already be gone.
            }

            try
            {
                if (!_mainForm.IsDisposed)
                {
                    _mainForm.Dispose();
                }
            }
            catch
            {
                // Shutdown should not be blocked by UI disposal errors.
            }

            _electronProcess.Dispose();
            base.ExitThreadCore();
        }

        private void ExitFromAnyThread()
        {
            if (_exiting)
            {
                return;
            }

            try
            {
                if (_mainForm.IsHandleCreated && !_mainForm.IsDisposed)
                {
                    _mainForm.BeginInvoke((Action)ExitThread);
                    return;
                }
            }
            catch
            {
                // Fall through to direct exit.
            }

            ExitThread();
        }
    }
}
