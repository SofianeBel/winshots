using System.Globalization;
using Winshots.App.Windows;

namespace Winshots.App.Capture;

public sealed record AgentWatchTarget
{
    public string? WindowHandle { get; init; }
    public string? TitleContains { get; init; }
    public string? ProcessName { get; init; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(WindowHandle) &&
            string.IsNullOrWhiteSpace(TitleContains) &&
            string.IsNullOrWhiteSpace(ProcessName))
        {
            throw new ArgumentException("Provide windowHandle, titleContains, or processName to select an Agent Watch target.");
        }
    }
}

public sealed record AgentWatchOptions
{
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 300_000;
    public const int MinPollIntervalMs = 100;
    public const int MaxPollIntervalMs = 5_000;
    public const int MinStableDurationMs = 100;
    public const int MaxStableDurationMs = 60_000;

    public int TimeoutMs { get; init; } = 10_000;
    public int PollIntervalMs { get; init; } = 500;
    public int StableDurationMs { get; init; } = 1_500;
    public int MinChangeHashDistance { get; init; } = 5;
    public int MaxStableHashDistance { get; init; } = 2;

    internal AgentWatchOptions Normalized()
    {
        return this with
        {
            TimeoutMs = Math.Clamp(TimeoutMs, MinTimeoutMs, MaxTimeoutMs),
            PollIntervalMs = Math.Clamp(PollIntervalMs, MinPollIntervalMs, MaxPollIntervalMs),
            StableDurationMs = Math.Clamp(StableDurationMs, MinStableDurationMs, MaxStableDurationMs),
            MinChangeHashDistance = Math.Clamp(MinChangeHashDistance, 1, 64),
            MaxStableHashDistance = Math.Clamp(MaxStableHashDistance, 0, 64)
        };
    }
}

public sealed record AgentWatchAppliedBounds
{
    public required int TimeoutMs { get; init; }
    public required int PollIntervalMs { get; init; }
    public int? StableDurationMs { get; init; }
    public int? MinChangeHashDistance { get; init; }
    public int? MaxStableHashDistance { get; init; }
}

public sealed record AgentWatchObservation
{
    public required string TimestampUtc { get; init; }
    public required bool WindowFound { get; init; }
    public string? WindowHandle { get; init; }
    public string? WindowTitle { get; init; }
    public string? ProcessName { get; init; }
    public int? ProcessId { get; init; }
    public string? TextSource { get; init; }
    public string? TextStatus { get; init; }
    public string? TextPreview { get; init; }
    public string? PerceptualHash { get; init; }
    public string? DirectoryPath { get; init; }
    public string? ScreenshotPath { get; init; }
    public string? TextPath { get; init; }
    public string? MetadataPath { get; init; }
    public string? Error { get; init; }

    internal string? MatchText { get; init; }

    internal ulong? ParsedHash => ulong.TryParse(
        PerceptualHash,
        NumberStyles.HexNumber,
        CultureInfo.InvariantCulture,
        out ulong hash)
        ? hash
        : null;
}

public sealed record AgentWatchResult
{
    public required string Condition { get; init; }
    public required string Outcome { get; init; }
    public required string Reason { get; init; }
    public required long DurationMs { get; init; }
    public required int FramesObserved { get; init; }
    public required int Comparisons { get; init; }
    public required AgentWatchAppliedBounds AppliedBounds { get; init; }
    public required AgentWatchTarget Target { get; init; }
    public string? TextContains { get; init; }
    public string? TextSource { get; init; }
    public bool? TargetObserved { get; init; }
    public int? LastHashDistance { get; init; }
    public long? StableForMs { get; init; }
    public AgentWatchObservation? BaselineObservation { get; init; }
    public AgentWatchObservation? LastObservation { get; init; }
}

public sealed class AgentWatchService
{
    private const int MaxTextPreviewCharacters = 1_000;
    private readonly IAgentWatchObservationSource _source;
    private readonly IAgentWatchScheduler _scheduler;

    public AgentWatchService(string captureRoot)
        : this(new WindowsAgentWatchObservationSource(captureRoot), new SystemAgentWatchScheduler())
    {
    }

    internal AgentWatchService(IAgentWatchObservationSource source, IAgentWatchScheduler scheduler)
    {
        _source = source;
        _scheduler = scheduler;
    }

    public Task<AgentWatchResult> WaitForWindowAsync(
        AgentWatchTarget target,
        AgentWatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        target.Validate();
        AgentWatchOptions applied = (options ?? new AgentWatchOptions()).Normalized();
        return RunAsync(
            "window_present",
            target,
            applied,
            includeText: false,
            includeImage: false,
            observation => observation.WindowFound
                ? Evaluation.Succeeded("A matching capturable window was observed.")
                : Evaluation.Pending(),
            cancellationToken);
    }

    public Task<AgentWatchResult> WaitForTextAsync(
        AgentWatchTarget target,
        string textContains,
        AgentWatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        target.Validate();
        if (string.IsNullOrWhiteSpace(textContains))
        {
            throw new ArgumentException("textContains is required.", nameof(textContains));
        }

        AgentWatchOptions applied = (options ?? new AgentWatchOptions()).Normalized();
        return RunAsync(
            "text_present",
            target,
            applied,
            includeText: true,
            includeImage: false,
            observation => observation.WindowFound && Contains(observation.MatchText, textContains)
                ? Evaluation.Succeeded("The case-insensitive text substring was observed in the current Windows UI Automation context.")
                : Evaluation.Pending(),
            cancellationToken,
            textContains,
            textSource: "windows_ui_automation");
    }

    public Task<AgentWatchResult> WaitForChangeAsync(
        AgentWatchTarget target,
        AgentWatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        target.Validate();
        AgentWatchOptions applied = (options ?? new AgentWatchOptions()).Normalized();
        AgentWatchObservation? baseline = null;

        return RunAsync(
            "visual_change",
            target,
            applied,
            includeText: false,
            includeImage: true,
            observation =>
            {
                if (!observation.WindowFound || observation.ParsedHash is not ulong currentHash)
                {
                    return Evaluation.Pending(baseline);
                }

                if (baseline?.ParsedHash is not ulong baselineHash)
                {
                    baseline = observation;
                    return Evaluation.Pending(baseline);
                }

                int distance = string.Equals(baseline.WindowHandle, observation.WindowHandle, StringComparison.OrdinalIgnoreCase)
                    ? PerceptualHash.Distance(baselineHash, currentHash)
                    : 64;
                return distance >= applied.MinChangeHashDistance
                    ? Evaluation.Succeeded(
                        $"The visual dHash distance {distance} reached the applied minimum {applied.MinChangeHashDistance}.",
                        baseline,
                        distance)
                    : Evaluation.WithComparison(baseline, distance);
            },
            cancellationToken);
    }

    public Task<AgentWatchResult> WaitForDisappearAsync(
        AgentWatchTarget target,
        string? textContains = null,
        AgentWatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        target.Validate();
        if (textContains is not null && string.IsNullOrWhiteSpace(textContains))
        {
            throw new ArgumentException("textContains must be omitted or contain non-whitespace text.", nameof(textContains));
        }

        AgentWatchOptions applied = (options ?? new AgentWatchOptions()).Normalized();
        bool targetObserved = false;
        bool inspectText = textContains is not null;

        return RunAsync(
            inspectText ? "text_disappeared" : "window_disappeared",
            target,
            applied,
            includeText: inspectText,
            includeImage: false,
            observation =>
            {
                bool present = observation.WindowFound && (!inspectText || Contains(observation.MatchText, textContains!));
                if (present)
                {
                    targetObserved = true;
                    return Evaluation.Pending(targetObserved: true);
                }

                return targetObserved
                    ? Evaluation.Succeeded(
                        inspectText
                            ? "The text was absent after it had been observed at least once."
                            : "The window was absent after it had been observed at least once.",
                        targetObserved: true)
                    : Evaluation.Pending(targetObserved: false);
            },
            cancellationToken,
            textContains,
            inspectText ? "windows_ui_automation" : null);
    }

    public Task<AgentWatchResult> WaitForStableAsync(
        AgentWatchTarget target,
        AgentWatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        target.Validate();
        AgentWatchOptions applied = (options ?? new AgentWatchOptions()).Normalized();
        AgentWatchObservation? stableBaseline = null;
        DateTimeOffset? stableSince = null;

        return RunAsync(
            "visual_stable",
            target,
            applied,
            includeText: false,
            includeImage: true,
            observation =>
            {
                DateTimeOffset now = _scheduler.UtcNow;
                if (!observation.WindowFound || observation.ParsedHash is not ulong currentHash)
                {
                    stableBaseline = null;
                    stableSince = null;
                    return Evaluation.Pending();
                }

                if (stableBaseline?.ParsedHash is not ulong baselineHash ||
                    !string.Equals(stableBaseline.WindowHandle, observation.WindowHandle, StringComparison.OrdinalIgnoreCase))
                {
                    stableBaseline = observation;
                    stableSince = now;
                    return Evaluation.Pending(stableBaseline, stableForMs: 0);
                }

                int distance = PerceptualHash.Distance(baselineHash, currentHash);
                if (distance > applied.MaxStableHashDistance)
                {
                    stableBaseline = observation;
                    stableSince = now;
                    return Evaluation.WithComparison(stableBaseline, distance, stableForMs: 0);
                }

                long stableForMs = (long)Math.Max(0, (now - stableSince!.Value).TotalMilliseconds);
                return stableForMs >= applied.StableDurationMs
                    ? Evaluation.Succeeded(
                        $"The visual dHash distance stayed at or below {applied.MaxStableHashDistance} for {stableForMs} ms.",
                        stableBaseline,
                        distance,
                        stableForMs: stableForMs)
                    : Evaluation.WithComparison(stableBaseline, distance, stableForMs);
            },
            cancellationToken);
    }

    private async Task<AgentWatchResult> RunAsync(
        string condition,
        AgentWatchTarget target,
        AgentWatchOptions options,
        bool includeText,
        bool includeImage,
        Func<AgentWatchObservation, Evaluation> evaluate,
        CancellationToken cancellationToken,
        string? textContains = null,
        string? textSource = null)
    {
        DateTimeOffset started = _scheduler.UtcNow;
        int framesObserved = 0;
        int comparisons = 0;
        int? lastHashDistance = null;
        long? stableForMs = null;
        bool? targetObserved = null;
        AgentWatchObservation? baseline = null;
        AgentWatchObservation? last = null;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan elapsedBeforeObservation = _scheduler.UtcNow - started;
                if (framesObserved > 0 && elapsedBeforeObservation.TotalMilliseconds >= options.TimeoutMs)
                {
                    return BuildResult("timed_out", "The applied timeout elapsed before the condition succeeded.");
                }

                TimeSpan remaining = TimeSpan.FromMilliseconds(options.TimeoutMs) - elapsedBeforeObservation;
                last = await _source.ObserveAsync(
                    target,
                    includeText,
                    includeImage,
                    remaining,
                    cancellationToken).ConfigureAwait(false);
                framesObserved++;

                Evaluation evaluation = evaluate(last);
                if (evaluation.Compared)
                {
                    comparisons++;
                }

                baseline = evaluation.Baseline ?? baseline;
                lastHashDistance = evaluation.HashDistance ?? lastHashDistance;
                stableForMs = evaluation.StableForMs ?? stableForMs;
                targetObserved = evaluation.TargetObserved ?? targetObserved;

                if (evaluation.Success)
                {
                    return BuildResult("succeeded", evaluation.Reason!);
                }

                TimeSpan elapsed = _scheduler.UtcNow - started;
                if (elapsed.TotalMilliseconds >= options.TimeoutMs)
                {
                    return BuildResult("timed_out", "The applied timeout elapsed before the condition succeeded.");
                }

                TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(
                    options.PollIntervalMs,
                    options.TimeoutMs - elapsed.TotalMilliseconds));
                await _scheduler.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BuildResult("cancelled", "The caller cancellation token was signalled.");
        }

        AgentWatchResult BuildResult(string outcome, string reason)
        {
            return new AgentWatchResult
            {
                Condition = condition,
                Outcome = outcome,
                Reason = reason,
                DurationMs = (long)Math.Max(0, (_scheduler.UtcNow - started).TotalMilliseconds),
                FramesObserved = framesObserved,
                Comparisons = comparisons,
                AppliedBounds = new AgentWatchAppliedBounds
                {
                    TimeoutMs = options.TimeoutMs,
                    PollIntervalMs = options.PollIntervalMs,
                    StableDurationMs = condition == "visual_stable" ? options.StableDurationMs : null,
                    MinChangeHashDistance = condition == "visual_change" ? options.MinChangeHashDistance : null,
                    MaxStableHashDistance = condition == "visual_stable" ? options.MaxStableHashDistance : null
                },
                Target = target,
                TextContains = textContains,
                TextSource = textSource,
                TargetObserved = targetObserved,
                LastHashDistance = lastHashDistance,
                StableForMs = stableForMs,
                BaselineObservation = baseline,
                LastObservation = last
            };
        }
    }

    private static bool Contains(string? value, string expected)
    {
        return value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record Evaluation(
        bool Success,
        string? Reason,
        bool Compared,
        AgentWatchObservation? Baseline,
        int? HashDistance,
        long? StableForMs,
        bool? TargetObserved)
    {
        public static Evaluation Pending(
            AgentWatchObservation? baseline = null,
            long? stableForMs = null,
            bool? targetObserved = null) =>
            new(false, null, false, baseline, null, stableForMs, targetObserved);

        public static Evaluation WithComparison(
            AgentWatchObservation? baseline,
            int hashDistance,
            long? stableForMs = null) =>
            new(false, null, true, baseline, hashDistance, stableForMs, null);

        public static Evaluation Succeeded(
            string reason,
            AgentWatchObservation? baseline = null,
            int? hashDistance = null,
            long? stableForMs = null,
            bool? targetObserved = null) =>
            new(true, reason, hashDistance is not null, baseline, hashDistance, stableForMs, targetObserved);
    }

    internal interface IAgentWatchObservationSource
    {
        Task<AgentWatchObservation> ObserveAsync(
            AgentWatchTarget target,
            bool includeText,
            bool includeImage,
            TimeSpan remaining,
            CancellationToken cancellationToken);
    }

    internal interface IAgentWatchScheduler
    {
        DateTimeOffset UtcNow { get; }
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    private sealed class SystemAgentWatchScheduler : IAgentWatchScheduler
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }

    private sealed class WindowsAgentWatchObservationSource(string captureRoot) : IAgentWatchObservationSource
    {
        private readonly CaptureWorkflow _workflow = new(captureRoot);
        private readonly UiAutomationTextExtractor _textExtractor = new();

        public Task<AgentWatchObservation> ObserveAsync(
            AgentWatchTarget target,
            bool includeText,
            bool includeImage,
            TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            WindowSnapshot? window = ResolveWindow(target);
            if (window is null)
            {
                return Task.FromResult(new AgentWatchObservation
                {
                    TimestampUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
                    WindowFound = false
                });
            }

            try
            {
                if (includeImage)
                {
                    CaptureResult capture = _workflow.CaptureWindow(window.Handle, "agent-watch");
                    string screenshotPath = capture.AvailableScreenshotPath ??
                        throw new InvalidOperationException(capture.Metadata.Diagnostics?.Image.Detail ?? "Agent Watch did not capture a valid image.");
                    ulong hash = PerceptualHash.Compute(screenshotPath);
                    string context = includeText && File.Exists(capture.TextPath)
                        ? File.ReadAllText(capture.TextPath)
                        : string.Empty;
                    return Task.FromResult(BuildObservation(
                        timestamp,
                        window,
                        includeText ? "windows_ui_automation_capture_context" : null,
                        includeText ? "captured" : null,
                        context,
                        hash,
                        capture));
                }

                if (includeText)
                {
                    TimeSpan textBudget = TimeSpan.FromMilliseconds(Math.Clamp(remaining.TotalMilliseconds, 100, 1_000));
                    TextExtractionResult text = _textExtractor.ExtractResult(window.Handle, textBudget);
                    return Task.FromResult(BuildObservation(
                        timestamp,
                        window,
                        "windows_ui_automation",
                        text.Status,
                        text.Text,
                        null,
                        null));
                }

                return Task.FromResult(BuildObservation(timestamp, window, null, null, null, null, null));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AgentWatchObservation
                {
                    TimestampUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
                    WindowFound = true,
                    WindowHandle = FormatHandle(window.Handle),
                    WindowTitle = window.Title,
                    ProcessName = window.ProcessName,
                    ProcessId = window.ProcessId,
                    Error = ex.Message
                });
            }
        }

        private static AgentWatchObservation BuildObservation(
            DateTimeOffset timestamp,
            WindowSnapshot window,
            string? textSource,
            string? textStatus,
            string? text,
            ulong? hash,
            CaptureResult? capture)
        {
            return new AgentWatchObservation
            {
                TimestampUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
                WindowFound = true,
                WindowHandle = FormatHandle(window.Handle),
                WindowTitle = window.Title,
                ProcessName = window.ProcessName,
                ProcessId = window.ProcessId,
                TextSource = textSource,
                TextStatus = textStatus,
                TextPreview = Truncate(text, MaxTextPreviewCharacters),
                MatchText = text,
                PerceptualHash = hash?.ToString("X16", CultureInfo.InvariantCulture),
                DirectoryPath = capture?.DirectoryPath,
                ScreenshotPath = capture?.AvailableScreenshotPath,
                TextPath = capture?.TextPath,
                MetadataPath = capture?.MetadataPath
            };
        }

        private static WindowSnapshot? ResolveWindow(AgentWatchTarget target)
        {
            if (!string.IsNullOrWhiteSpace(target.WindowHandle))
            {
                IntPtr handle = ParseHandle(target.WindowHandle);
                if (!NativeMethods.IsUsableCaptureTarget(handle))
                {
                    return null;
                }

                WindowSnapshot exact = NativeMethods.GetWindowSnapshot(handle);
                return Matches(exact, target) ? exact : null;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            return NativeMethods.EnumerateCapturableWindows()
                .Where(window => Matches(window, target))
                .OrderByDescending(window => window.Handle == foreground)
                .ThenBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(window => window.Handle.ToInt64())
                .FirstOrDefault();
        }

        private static bool Matches(WindowSnapshot window, AgentWatchTarget target)
        {
            return (string.IsNullOrWhiteSpace(target.TitleContains) ||
                    window.Title.Contains(target.TitleContains, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(target.ProcessName) ||
                    NormalizeProcess(window.ProcessName).Contains(NormalizeProcess(target.ProcessName), StringComparison.OrdinalIgnoreCase));
        }

        private static IntPtr ParseHandle(string value)
        {
            string normalized = value.Trim();
            NumberStyles style = NumberStyles.Integer;
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[2..];
                style = NumberStyles.AllowHexSpecifier;
            }

            if (!long.TryParse(normalized, style, CultureInfo.InvariantCulture, out long handle) || handle == 0)
            {
                throw new ArgumentException($"Window handle '{value}' is invalid.", nameof(value));
            }

            return new IntPtr(handle);
        }

        private static string NormalizeProcess(string value)
        {
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
        }

        private static string FormatHandle(IntPtr handle)
        {
            return $"0x{handle.ToInt64():X}";
        }

        private static string? Truncate(string? value, int maxCharacters)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= maxCharacters ? value : value[..maxCharacters] + "...";
        }
    }
}
