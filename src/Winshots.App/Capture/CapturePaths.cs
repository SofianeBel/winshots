namespace Winshots.App.Capture;

public static class CapturePaths
{
    public static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Winshots",
            "captures");

    public static string DefaultSessionRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Winshots",
            "sessions");
}
