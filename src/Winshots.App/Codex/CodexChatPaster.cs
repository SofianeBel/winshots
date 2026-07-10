using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Specialized;
using System.Text;
using System.Windows.Automation;
using Winshots.App.Capture;
using Winshots.App.Windows;

namespace Winshots.App.Codex;

public sealed record CodexPasteResult(bool Success, string Message);

public static class CodexChatPaster
{
    public static CodexPasteResult TryPasteCapture(CaptureResult capture)
    {
        if (!TryGetRequiredAttachmentPaths(capture, out string[] attachmentPaths, out string attachmentError))
        {
            return new CodexPasteResult(false, attachmentError);
        }

        CodexWindow? target = FindCodexWindow();
        if (target is null && !IsCodexProcessRunning())
        {
            return new CodexPasteResult(false, $"Codex App is not running. Capture saved at {capture.DirectoryPath}");
        }

        if (target is null)
        {
            return new CodexPasteResult(false, "Codex App is running, but no visible window was found.");
        }

        IntPtr window = target.Handle;
        _ = NativeMethods.ShowWindow(window, NativeMethods.SwRestore);
        _ = NativeMethods.SetForegroundWindow(window);
        Thread.Sleep(250);

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (NativeMethods.GetProcessId(foreground) != target.ProcessId)
        {
            return new CodexPasteResult(false, "Windows did not give focus to Codex App. Open the chat and paste the capture manually.");
        }

        if (!TryFocusCodexComposer(window, target.ProcessId, out string focusError))
        {
            return new CodexPasteResult(false, $"{focusError} Capture saved at {capture.DirectoryPath}. Attach screenshot.png, metadata.json, and context.txt manually.");
        }

        Clipboard.SetFileDropList(ToFileDropList(attachmentPaths));
        SendKeys.SendWait("^v");
        Thread.Sleep(500);

        if (!AreAttachmentNamesVisibleNearFocusedComposer(attachmentPaths))
        {
            return new CodexPasteResult(false, $"Codex composer was focused, but Winshots could not confirm that screenshot.png, metadata.json, and context.txt were attached. Capture saved at {capture.DirectoryPath}.");
        }

        return new CodexPasteResult(true, "Attached screenshot.png, metadata.json, and context.txt to the Codex chat composer.");
    }

    public static string BuildPrompt(CaptureResult capture, string context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Winshots capture attached for this Codex chat.");
        builder.AppendLine($"Window: {capture.Metadata.WindowTitle}");
        builder.AppendLine($"Process: {capture.Metadata.ProcessName} ({capture.Metadata.ProcessId})");
        builder.AppendLine("Attached files: screenshot.png, metadata.json, context.txt");
        return builder.ToString();
    }

    public static IReadOnlyList<string> RequiredAttachmentPaths(CaptureResult capture)
    {
        return [capture.ScreenshotPath, capture.MetadataPath, capture.TextPath];
    }

    public static bool TryGetRequiredAttachmentPaths(CaptureResult capture, out string[] paths, out string error)
    {
        paths = RequiredAttachmentPaths(capture).ToArray();
        string? missing = paths.FirstOrDefault(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path));
        if (missing is not null)
        {
            error = $"Capture saved at {capture.DirectoryPath}, but a required attachment is missing: {missing}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsLikelyChatTextInput(
        string controlTypeProgrammaticName,
        bool isKeyboardFocusable,
        bool supportsTextPattern,
        bool supportsValuePattern,
        string name)
    {
        if (!isKeyboardFocusable || IsRejectedFocusName(name))
        {
            return false;
        }

        return string.Equals(controlTypeProgrammaticName, ControlType.Edit.ProgrammaticName, StringComparison.Ordinal) ||
            string.Equals(controlTypeProgrammaticName, ControlType.Document.ProgrammaticName, StringComparison.Ordinal) ||
            (supportsTextPattern || supportsValuePattern) &&
            (string.Equals(controlTypeProgrammaticName, ControlType.Group.ProgrammaticName, StringComparison.Ordinal) ||
             string.Equals(controlTypeProgrammaticName, ControlType.Pane.ProgrammaticName, StringComparison.Ordinal) ||
             string.Equals(controlTypeProgrammaticName, ControlType.Custom.ProgrammaticName, StringComparison.Ordinal));
    }

    private static bool TryFocusCodexComposer(IntPtr window, int processId, out string error)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(window);
            if (IsFocusedCodexComposer(processId))
            {
                error = string.Empty;
                return true;
            }

            foreach (ComposerCandidate candidate in FindComposerCandidates(root))
            {
                if (!TrySetFocus(candidate.Element))
                {
                    continue;
                }

                Thread.Sleep(150);
                if (IsFocusedCodexComposer(processId))
                {
                    error = string.Empty;
                    return true;
                }
            }

            error = "Codex App is focused, but Winshots could not safely identify and focus the chat composer.";
            return false;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            error = "Codex App is focused, but UI Automation could not inspect the chat composer.";
            return false;
        }
    }

    private static bool IsFocusedCodexComposer(int processId)
    {
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (ReadInt(focused, AutomationElement.ProcessIdProperty) != processId || IsInRejectedFocusArea(focused))
            {
                return false;
            }

            ControlType controlType = ReadControlType(focused);
            bool isKeyboardFocusable = ReadBool(focused, AutomationElement.IsKeyboardFocusableProperty);
            bool supportsTextPattern = SupportsPattern(focused, TextPattern.Pattern);
            bool supportsValuePattern = SupportsPattern(focused, ValuePattern.Pattern);
            string name = ReadString(focused, AutomationElement.NameProperty);

            return IsLikelyChatTextInput(controlType.ProgrammaticName, isKeyboardFocusable, supportsTextPattern, supportsValuePattern, name) &&
                HasComposerAnchorNearFocusedElement(focused);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static IEnumerable<ComposerCandidate> FindComposerCandidates(AutomationElement root)
    {
        var candidates = new List<ComposerCandidate>();
        foreach (AutomationElement element in EnumerateDescendants(root, maxNodes: 2400))
        {
            ControlType controlType = ReadControlType(element);
            bool isKeyboardFocusable = ReadBool(element, AutomationElement.IsKeyboardFocusableProperty);
            bool supportsTextPattern = SupportsPattern(element, TextPattern.Pattern);
            bool supportsValuePattern = SupportsPattern(element, ValuePattern.Pattern);
            string name = ReadString(element, AutomationElement.NameProperty);

            if (!IsLikelyChatTextInput(controlType.ProgrammaticName, isKeyboardFocusable, supportsTextPattern, supportsValuePattern, name) ||
                IsInRejectedFocusArea(element))
            {
                continue;
            }

            int anchorCount = CountComposerAnchorsNearElement(element);
            if (anchorCount < 2)
            {
                continue;
            }

            int score = ScoreComposerCandidate(controlType.ProgrammaticName, supportsTextPattern, supportsValuePattern, name, anchorCount);
            candidates.Add(new ComposerCandidate(element, score));
        }

        return candidates.OrderByDescending(candidate => candidate.Score);
    }

    private static bool IsInRejectedFocusArea(AutomationElement element)
    {
        foreach (AutomationElement item in EnumerateSelfAndParents(element, maxDepth: 10))
        {
            if (IsRejectedFocusName(ReadString(item, AutomationElement.NameProperty)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySetFocus(AutomationElement element)
    {
        try
        {
            element.SetFocus();
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static bool IsRejectedFocusName(string name)
    {
        return ContainsOrdinalIgnoreCase(name, "Terminal input") ||
            ContainsOrdinalIgnoreCase(name, "PowerShell") ||
            ContainsOrdinalIgnoreCase(name, "Filter sidebar chats") ||
            string.Equals(name.Trim(), "Search", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasComposerAnchorNearFocusedElement(AutomationElement focused)
    {
        return CountComposerAnchorsNearElement(focused) >= 2;
    }

    private static int CountComposerAnchorsNearElement(AutomationElement element)
    {
        foreach (AutomationElement item in EnumerateSelfAndParents(element, maxDepth: 8))
        {
            int count = CountComposerAnchors(item, maxNodes: 160);
            if (count >= 2)
            {
                return count;
            }
        }

        return 0;
    }

    private static int CountComposerAnchors(AutomationElement root, int maxNodes)
    {
        int visited = 0;
        var anchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Add files and more",
            "Dictate",
            "Environment",
            "Create environment",
            "Approve for me"
        };

        return CountComposerAnchors(root, anchors, ref visited, maxNodes);
    }

    private static int ScoreComposerCandidate(
        string controlTypeProgrammaticName,
        bool supportsTextPattern,
        bool supportsValuePattern,
        string name,
        int anchorCount)
    {
        int score = anchorCount * 100;
        if (supportsTextPattern)
        {
            score += 30;
        }

        if (supportsValuePattern)
        {
            score += 15;
        }

        if (string.Equals(controlTypeProgrammaticName, ControlType.Group.ProgrammaticName, StringComparison.Ordinal) ||
            string.Equals(controlTypeProgrammaticName, ControlType.Custom.ProgrammaticName, StringComparison.Ordinal) ||
            string.Equals(controlTypeProgrammaticName, ControlType.Pane.ProgrammaticName, StringComparison.Ordinal))
        {
            score += 20;
        }

        if (string.Equals(controlTypeProgrammaticName, ControlType.Document.ProgrammaticName, StringComparison.Ordinal))
        {
            score -= 20;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            score += 5;
        }
        else if (name.Length > 160)
        {
            score -= 15;
        }

        return score;
    }

    private static int CountComposerAnchors(
        AutomationElement element,
        HashSet<string> anchors,
        ref int visited,
        int maxNodes)
    {
        if (visited >= maxNodes)
        {
            return 0;
        }

        visited++;
        int count = anchors.Contains(ReadString(element, AutomationElement.NameProperty)) ? 1 : 0;

        AutomationElement? child = SafeGetChild(element);
        while (child is not null && visited < maxNodes)
        {
            count += CountComposerAnchors(child, anchors, ref visited, maxNodes);
            child = SafeGetSibling(child);
        }

        return count;
    }

    private static IEnumerable<AutomationElement> EnumerateSelfAndParents(AutomationElement element, int maxDepth)
    {
        AutomationElement? current = element;
        for (int i = 0; current is not null && i < maxDepth; i++)
        {
            yield return current;
            current = SafeGetParent(current);
        }
    }

    private static IEnumerable<AutomationElement> EnumerateDescendants(AutomationElement root, int maxNodes)
    {
        var queue = new Queue<AutomationElement>();
        AutomationElement? child = SafeGetChild(root);
        while (child is not null)
        {
            queue.Enqueue(child);
            child = SafeGetSibling(child);
        }

        int visited = 0;
        while (queue.Count > 0 && visited < maxNodes)
        {
            AutomationElement current = queue.Dequeue();
            visited++;
            yield return current;

            child = SafeGetChild(current);
            while (child is not null)
            {
                queue.Enqueue(child);
                child = SafeGetSibling(child);
            }
        }
    }

    private static AutomationElement? SafeGetParent(AutomationElement element)
    {
        try
        {
            return TreeWalker.ControlViewWalker.GetParent(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static AutomationElement? SafeGetChild(AutomationElement element)
    {
        try
        {
            return TreeWalker.ControlViewWalker.GetFirstChild(element);
        }
        catch (ElementNotAvailableException)
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
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static ControlType ReadControlType(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType;
        }
        catch (ElementNotAvailableException)
        {
            return ControlType.Custom;
        }
    }

    private static string ReadString(AutomationElement element, AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(property, ignoreDefaultValue: true) as string ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static int ReadInt(AutomationElement element, AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(property, ignoreDefaultValue: true) is int value ? value : 0;
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
    }

    private static bool ReadBool(AutomationElement element, AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(property, ignoreDefaultValue: true) is true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool SupportsPattern(AutomationElement element, AutomationPattern pattern)
    {
        try
        {
            return element.TryGetCurrentPattern(pattern, out _);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static StringCollection ToFileDropList(IEnumerable<string> paths)
    {
        var files = new StringCollection();
        files.AddRange(paths.ToArray());
        return files;
    }

    private static bool AreAttachmentNamesVisibleNearFocusedComposer(IEnumerable<string> attachmentPaths)
    {
        try
        {
            var expectedNames = new HashSet<string>(
                attachmentPaths.Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name))!,
                StringComparer.OrdinalIgnoreCase);

            if (expectedNames.Count == 0)
            {
                return false;
            }

            AutomationElement focused = AutomationElement.FocusedElement;
            AutomationElement? composerScope = FindNearestComposerScope(focused);
            if (composerScope is null)
            {
                return false;
            }

            RemoveVisibleAttachmentNames(expectedNames, ReadString(composerScope, AutomationElement.NameProperty));
            foreach (AutomationElement element in EnumerateDescendants(composerScope, maxNodes: 500))
            {
                RemoveVisibleAttachmentNames(expectedNames, ReadString(element, AutomationElement.NameProperty));
                if (expectedNames.Count == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static AutomationElement? FindNearestComposerScope(AutomationElement element)
    {
        foreach (AutomationElement item in EnumerateSelfAndParents(element, maxDepth: 8))
        {
            if (CountComposerAnchors(item, maxNodes: 160) >= 2)
            {
                return item;
            }
        }

        return null;
    }

    private static void RemoveVisibleAttachmentNames(HashSet<string> expectedNames, string visibleName)
    {
        if (string.IsNullOrWhiteSpace(visibleName))
        {
            return;
        }

        foreach (string expectedName in expectedNames.ToArray())
        {
            if (visibleName.Contains(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                expectedNames.Remove(expectedName);
            }
        }
    }

    private static CodexWindow? FindCodexWindow()
    {
        foreach (IntPtr window in NativeMethods.EnumerateTopLevelWindows())
        {
            if (!NativeMethods.IsVisibleWindow(window))
            {
                continue;
            }

            int processId = NativeMethods.GetProcessId(window);
            if (processId <= 0)
            {
                continue;
            }

            try
            {
                using Process process = Process.GetProcessById(processId);
                if (IsCodexProcessName(process.ProcessName))
                {
                    return new CodexWindow(processId, window);
                }
            }
            catch
            {
                // Windows can close windows between EnumWindows and process lookup.
            }
        }

        return null;
    }

    private static bool IsCodexProcessRunning()
    {
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (IsCodexProcessName(process.ProcessName))
                {
                    process.Dispose();
                    return true;
                }

                process.Dispose();
            }
            catch
            {
                process.Dispose();
            }
        }

        return false;
    }

    public static bool IsCodexProcessName(string processName)
    {
        return string.Equals(processName, "codex", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(processName, "chatgpt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOrdinalIgnoreCase(string value, string expected)
    {
        return value.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CodexWindow(int ProcessId, IntPtr Handle);

    private sealed record ComposerCandidate(AutomationElement Element, int Score);
}
