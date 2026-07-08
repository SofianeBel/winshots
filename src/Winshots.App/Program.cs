using Winshots.App.Capture;
using Winshots.App.UI;
using Winshots.App.Windows;

namespace Winshots.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "capture-once", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = RunCaptureOnce(args[1..]);
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

    private static int RunCaptureOnce(string[] args)
    {
        try
        {
            string outputRoot = CapturePaths.DefaultRoot;
            int delayMs = 0;

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
            }

            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }

            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (!NativeMethods.IsUsableCaptureTarget(hwnd))
            {
                throw new InvalidOperationException("No usable foreground window is available to capture.");
            }

            var workflow = new CaptureWorkflow(outputRoot);
            CaptureResult result = workflow.CaptureWindow(hwnd, "cli");
            Console.WriteLine(result.DirectoryPath);
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

            recorder.Start(NativeMethods.GetForegroundWindow);
            VisualSessionManifest manifest = await recorder.WaitForCompletionAsync().ConfigureAwait(false);

            Console.WriteLine(manifest.DirectoryPath);
            if (!string.IsNullOrWhiteSpace(manifest.VideoPath))
            {
                Console.WriteLine(manifest.VideoPath);
            }
            else if (!string.IsNullOrWhiteSpace(manifest.VideoError))
            {
                Console.Error.WriteLine(manifest.VideoError);
            }

            return manifest.CapturedFrameCount > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
