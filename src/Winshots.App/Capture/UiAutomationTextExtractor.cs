using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Diagnostics;

namespace Winshots.App.Capture;

public sealed class UiAutomationTextExtractor
{
    private const int MaxNodes = 2500;
    private const int MaxTextLength = 200_000;
    private const int MaxTextPatternLength = 20_000;
    private const int TimedOutWindowCooldownMs = 60_000;
    private static readonly ConcurrentDictionary<IntPtr, long> TimedOutWindows = new();

    public string Extract(IntPtr hwnd)
    {
        return ExtractResult(hwnd, CaptureOptions.Default.TextExtractionTimeout).Text;
    }

    public TextExtractionResult ExtractResult(IntPtr hwnd, TimeSpan maxDuration)
    {
        TimeSpan timeout = maxDuration <= TimeSpan.Zero
            ? CaptureOptions.Default.TextExtractionTimeout
            : maxDuration;
        long now = Environment.TickCount64;
        if (TimedOutWindows.TryGetValue(hwnd, out long retryAfter) && retryAfter > now)
        {
            return TimedOutResult(timeout, "was skipped because this window timed out recently");
        }

        var completion = new TaskCompletionSource<TextExtractionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(ExtractResultCore(hwnd, timeout));
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new TextExtractionResult(
                    $"[UI Automation text extraction failed: {ex.Message}]",
                    0,
                    false,
                    false,
                    false));
            }
        })
        {
            IsBackground = true,
            Name = "Winshots UI Automation"
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();

        if (completion.Task.Wait(timeout + TimeSpan.FromMilliseconds(100)))
        {
            TimedOutWindows.TryRemove(hwnd, out _);
            return completion.Task.Result;
        }

        TimedOutWindows[hwnd] = Environment.TickCount64 + TimedOutWindowCooldownMs;
        return TimedOutResult(timeout, "timed out");
    }

    private static TextExtractionResult TimedOutResult(TimeSpan timeout, string status)
    {
        return new TextExtractionResult(
            $"[UI Automation text extraction {status} ({timeout.TotalMilliseconds:0}ms limit).]",
            0,
            false,
            false,
            true);
    }

    private TextExtractionResult ExtractResultCore(IntPtr hwnd, TimeSpan maxDuration)
    {
        var state = new ExtractionState(Stopwatch.StartNew(), maxDuration);

        try
        {
            var builder = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (!TryAppendCachedTree(hwnd, builder, seen, state))
            {
                builder.Clear();
                seen.Clear();
                state.ResetTraversal();
                AppendElement(AutomationElement.FromHandle(hwnd), builder, seen, 0, state);
            }

            return new TextExtractionResult(
                TrimText(builder.ToString(), state),
                state.NodeCount,
                state.NodeLimitReached,
                state.TextLimitReached,
                state.TimedOut);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return new TextExtractionResult(
                $"[UI Automation text extraction failed: {ex.Message}]",
                state.NodeCount,
                state.NodeLimitReached,
                state.TextLimitReached,
                state.TimedOut);
        }
    }

    private static bool TryAppendCachedTree(
        IntPtr hwnd,
        StringBuilder builder,
        HashSet<string> seen,
        ExtractionState state)
    {
        try
        {
            var request = new CacheRequest
            {
                TreeFilter = Automation.ControlViewCondition,
                TreeScope = TreeScope.Subtree
            };
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.NameProperty);

            AutomationElement root = AutomationElement.FromHandle(hwnd);
            AutomationElementCollection children;
            using (request.Activate())
            {
                children = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            }

            state.NodeCount++;
            AppendLine(
                builder,
                seen,
                0,
                ReadControlType(root),
                ReadString(root, AutomationElement.NameProperty),
                string.Empty,
                string.Empty);

            foreach (AutomationElement child in children)
            {
                AppendCachedElement(child, builder, seen, 1, state);
            }

            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static void AppendCachedElement(
        AutomationElement element,
        StringBuilder builder,
        HashSet<string> seen,
        int depth,
        ExtractionState state)
    {
        if (ShouldStop(builder, state))
        {
            return;
        }

        state.NodeCount++;

        AutomationElement[] children = element.CachedChildren.Cast<AutomationElement>().ToArray();
        ControlType? controlType = ReadCachedControlType(element);
        string name = ReadCachedString(element, AutomationElement.NameProperty);
        string value = ShouldReadValue(controlType) ? ReadValue(element) : string.Empty;
        string text = children.Length == 0 && ShouldReadText(controlType) ? ReadText(element) : string.Empty;

        AppendLine(builder, seen, depth, controlType, name, value, text);

        foreach (AutomationElement child in children)
        {
            if (ShouldStop(builder, state))
            {
                break;
            }

            AppendCachedElement(child, builder, seen, depth + 1, state);
        }
    }

    private static void AppendElement(
        AutomationElement element,
        StringBuilder builder,
        HashSet<string> seen,
        int depth,
        ExtractionState state)
    {
        if (ShouldStop(builder, state))
        {
            return;
        }

        state.NodeCount++;

        AutomationElement? child = SafeGetChild(element);
        ControlType? controlType = ReadControlType(element);
        string name = ReadString(element, AutomationElement.NameProperty);
        string value = ShouldReadValue(controlType) ? ReadValue(element) : string.Empty;
        string text = child is null && ShouldReadText(controlType) ? ReadText(element) : string.Empty;

        AppendLine(builder, seen, depth, controlType, name, value, text);

        while (child is not null && !ShouldStop(builder, state))
        {
            AppendElement(child, builder, seen, depth + 1, state);
            child = SafeGetSibling(child);
        }
    }

    private static void AppendLine(
        StringBuilder builder,
        HashSet<string> seen,
        int depth,
        ControlType? controlType,
        string name,
        string value,
        string text)
    {
        var parts = new List<string>();
        if (controlType is not null)
        {
            parts.Add(controlType.LocalizedControlType);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, name, StringComparison.Ordinal))
        {
            parts.Add($"value={value}");
        }

        if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, name, StringComparison.Ordinal))
        {
            parts.Add($"text={text}");
        }

        if (parts.Count > 0)
        {
            string line = $"{new string(' ', depth * 2)}- {string.Join(": ", parts.Select(CleanLine))}";
            if (seen.Add(line))
            {
                builder.AppendLine(line);
            }
        }
    }

    private static bool ShouldStop(StringBuilder builder, ExtractionState state)
    {
        if (state.NodeCount >= MaxNodes)
        {
            state.NodeLimitReached = true;
            return true;
        }

        if (builder.Length >= MaxTextLength)
        {
            state.TextLimitReached = true;
            return true;
        }

        if (state.Stopwatch.Elapsed >= state.MaxDuration)
        {
            state.TimedOut = true;
            return true;
        }

        return false;
    }

    private static AutomationElement? SafeGetChild(AutomationElement element)
    {
        try
        {
            return TreeWalker.ControlViewWalker.GetFirstChild(element);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static AutomationElement? SafeGetSibling(AutomationElement element)
    {
        try
        {
            return TreeWalker.ControlViewWalker.GetNextSibling(element);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static bool ShouldReadValue(ControlType? controlType)
    {
        return controlType == ControlType.Edit ||
            controlType == ControlType.Document ||
            controlType == ControlType.ComboBox ||
            controlType == ControlType.Hyperlink ||
            controlType == ControlType.ProgressBar ||
            controlType == ControlType.Slider ||
            controlType == ControlType.Spinner;
    }

    private static bool ShouldReadText(ControlType? controlType)
    {
        return controlType == ControlType.Document || controlType == ControlType.Edit;
    }

    private static ControlType? ReadCachedControlType(AutomationElement element)
    {
        object value = element.GetCachedPropertyValue(AutomationElement.ControlTypeProperty, true);
        return value as ControlType;
    }

    private static string ReadCachedString(AutomationElement element, AutomationProperty property)
    {
        object value = element.GetCachedPropertyValue(property, true);
        return value as string ?? string.Empty;
    }

    private static ControlType? ReadControlType(AutomationElement element)
    {
        try
        {
            object value = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
            return value as ControlType;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static string ReadString(AutomationElement element, AutomationProperty property)
    {
        try
        {
            object value = element.GetCurrentPropertyValue(property, true);
            return value is string text ? text : string.Empty;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return string.Empty;
        }
    }

    private static string ReadValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern) &&
                pattern is ValuePattern valuePattern)
            {
                return valuePattern.Current.Value ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
        }

        return string.Empty;
    }

    private static string ReadText(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern) &&
                pattern is TextPattern textPattern)
            {
                return textPattern.DocumentRange.GetText(MaxTextPatternLength);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
        }

        return string.Empty;
    }

    private static string CleanLine(string value)
    {
        string collapsed = string.Join(" ", value.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 800 ? collapsed : collapsed[..800] + "...";
    }

    private static string TrimText(string value, ExtractionState state)
    {
        if (value.Length <= MaxTextLength)
        {
            return value;
        }

        state.TextLimitReached = true;
        return value[..MaxTextLength] + Environment.NewLine + "[truncated]";
    }

    private sealed class ExtractionState(Stopwatch stopwatch, TimeSpan maxDuration)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public TimeSpan MaxDuration { get; } = maxDuration <= TimeSpan.Zero
            ? CaptureOptions.Default.TextExtractionTimeout
            : maxDuration;
        public int NodeCount { get; set; }
        public bool NodeLimitReached { get; set; }
        public bool TextLimitReached { get; set; }
        public bool TimedOut { get; set; }

        public void ResetTraversal()
        {
            NodeCount = 0;
            NodeLimitReached = false;
            TextLimitReached = false;
            TimedOut = false;
        }
    }
}
