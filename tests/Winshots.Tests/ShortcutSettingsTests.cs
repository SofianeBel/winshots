using Winshots.App.Codex;
using Winshots.App.Capture;
using Winshots.App.Settings;
using Winshots.App.Windows;
using System.Windows.Forms;

namespace Winshots.Tests;

public sealed class ShortcutSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Winshots.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void HotkeyBinding_TryParse_ParsesModifiersAndKey()
    {
        bool parsed = HotkeyBinding.TryParse("Ctrl+Shift+Enter", out HotkeyBinding? hotkey, out string error);

        Assert.True(parsed, error);
        Assert.NotNull(hotkey);
        Assert.Equal(NativeMethods.ModControl | NativeMethods.ModShift, hotkey.Modifiers);
        Assert.Equal(Keys.Enter, hotkey.Key);
        Assert.Equal("Ctrl+Shift+Enter", hotkey.ToString());
    }

    [Fact]
    public void HotkeyBinding_TryParseOptional_AllowsDisabledShortcut()
    {
        bool parsed = HotkeyBinding.TryParseOptional("None", out HotkeyBinding? hotkey, out string error);

        Assert.True(parsed, error);
        Assert.Null(hotkey);
    }

    [Fact]
    public void HotkeyBinding_TryParse_RejectsShortcutWithoutModifier()
    {
        bool parsed = HotkeyBinding.TryParse("F12", out HotkeyBinding? hotkey, out string error);

        Assert.False(parsed);
        Assert.Null(hotkey);
        Assert.Contains("modifier", error);
    }

    [Fact]
    public void ShortcutSettingsStore_Load_ReturnsDefaults_WhenFileIsMissing()
    {
        var store = new ShortcutSettingsStore(Path.Combine(_root, "settings.json"));

        ShortcutSettings settings = store.Load();

        Assert.Equal("Ctrl+Shift+Space", settings.CaptureHotkey);
        Assert.Equal("Ctrl+Shift+Enter", settings.CaptureToCodexHotkey);
    }

    [Fact]
    public void ShortcutSettingsStore_Save_RoundTripsSettings()
    {
        string path = Path.Combine(_root, "settings.json");
        var store = new ShortcutSettingsStore(path);
        var settings = new ShortcutSettings
        {
            CaptureHotkey = "Ctrl+Shift+F8",
            CaptureToCodexHotkey = "Ctrl+Shift+F9"
        };

        store.Save(settings);

        ShortcutSettings loaded = store.Load();
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void CodexChatPaster_BuildPrompt_DescribesAttachedFilesWithoutInliningContext()
    {
        CaptureResult result = CreateCaptureResult("C:\\captures");

        string prompt = CodexChatPaster.BuildPrompt(result, "Visible button text");

        Assert.Contains("Window", prompt);
        Assert.Contains("screenshot.png", prompt);
        Assert.Contains("metadata.json", prompt);
        Assert.Contains("context.txt", prompt);
        Assert.DoesNotContain("Visible button text", prompt);
    }

    [Fact]
    public void CodexChatPaster_TryGetRequiredAttachmentPaths_ReturnsAllCaptureFiles()
    {
        Directory.CreateDirectory(_root);
        CaptureResult result = CreateCaptureResult(_root);
        File.WriteAllBytes(result.ScreenshotPath, [1, 2, 3]);
        File.WriteAllText(result.MetadataPath, "{}");
        File.WriteAllText(result.TextPath, "context");

        bool ok = CodexChatPaster.TryGetRequiredAttachmentPaths(result, out string[] paths, out string error);

        Assert.True(ok, error);
        Assert.Equal([result.ScreenshotPath, result.MetadataPath, result.TextPath], paths);
    }

    [Fact]
    public void CodexChatPaster_TryGetRequiredAttachmentPaths_RejectsMissingFile()
    {
        Directory.CreateDirectory(_root);
        CaptureResult result = CreateCaptureResult(_root);
        File.WriteAllBytes(result.ScreenshotPath, [1, 2, 3]);
        File.WriteAllText(result.TextPath, "context");

        bool ok = CodexChatPaster.TryGetRequiredAttachmentPaths(result, out _, out string error);

        Assert.False(ok);
        Assert.Contains("metadata.json", error);
        Assert.Contains(_root, error);
    }

    [Fact]
    public void CodexChatPaster_IsLikelyChatTextInput_AllowsFocusableTextGroup()
    {
        bool isInput = CodexChatPaster.IsLikelyChatTextInput(
            "ControlType.Group",
            isKeyboardFocusable: true,
            supportsTextPattern: true,
            supportsValuePattern: false,
            name: string.Empty);

        Assert.True(isInput);
    }

    [Fact]
    public void CodexChatPaster_IsLikelyChatTextInput_RejectsTerminalInput()
    {
        bool isInput = CodexChatPaster.IsLikelyChatTextInput(
            "ControlType.Edit",
            isKeyboardFocusable: true,
            supportsTextPattern: false,
            supportsValuePattern: true,
            name: "Terminal input");

        Assert.False(isInput);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CaptureResult CreateCaptureResult(string directory)
    {
        string screenshot = Path.Combine(directory, "screenshot.png");
        string context = Path.Combine(directory, "context.txt");
        string metadataPath = Path.Combine(directory, "metadata.json");
        var metadata = new CaptureMetadata
        {
            Id = "capture",
            TimestampUtc = "2026-06-20T10:00:00.0000000Z",
            TimestampLocal = "2026-06-20 12:00:00 +02:00",
            Reason = "test",
            WindowTitle = "Window",
            ProcessName = "notepad",
            ProcessId = 123,
            WindowHandle = "0x123",
            Bounds = new CaptureBounds(1, 2, 300, 200),
            ScreenshotPath = screenshot,
            TextPath = context,
            ExtractedTextLength = 5
        };

        return new CaptureResult(metadata, directory, screenshot, context, metadataPath);
    }
}
