namespace Winshots.App.Settings;

public sealed record ShortcutSettings
{
    public string CaptureHotkey { get; init; } = HotkeyBinding.DefaultCapture.ToString();

    public string CaptureToCodexHotkey { get; init; } = HotkeyBinding.DefaultCaptureToCodex.ToString();
}
