namespace Winshots.App.Capture;

public static class CapturePaths
{
    public static string DefaultRoot => ResolveOverride(
        "WINSHOTS_CAPTURE_ROOT",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Winshots",
            "captures"));

    public static string DefaultSessionRoot => ResolveOverride(
        "WINSHOTS_SESSION_ROOT",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Winshots",
            "sessions"));

    public static string DefaultInstantReplayRoot => ResolveOverride(
        "WINSHOTS_REPLAY_ROOT",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Winshots",
            "instant-replay"));

    private static string ResolveOverride(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : Path.GetFullPath(value);
    }
}
