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

    public string Extract(IntPtr hwnd)
    {
        return ExtractResult(hwnd, CaptureOptions.Default.TextExtractionTimeout).Text;
    }

    public TextExtractionResult ExtractResult(IntPtr hwnd, TimeSpan maxDuration)
    {
        var state = new ExtractionState(Stopwatch.StartNew(), maxDuration);

        try
        {
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            var builder = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            AppendElement(root, builder, seen, 0, state);
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

        string controlType = ReadControlType(element);
        string name = ReadString(element, AutomationElement.NameProperty);
        string value = ReadValue(element);
        string text = ReadText(element);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(controlType))
        {
            parts.Add(controlType);
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

        AutomationElement? child = SafeGetChild(element);
        while (child is not null && !ShouldStop(builder, state))
        {
            AppendElement(child, builder, seen, depth + 1, state);
            child = SafeGetSibling(child);
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

    private static string ReadControlType(AutomationElement element)
    {
        try
        {
            object value = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
            return value is ControlType controlType ? controlType.LocalizedControlType : string.Empty;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return string.Empty;
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
    }
}
