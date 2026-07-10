using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Winshots.App.Capture;

namespace Winshots.App.Windows;

public static class NativeMethods
{
    public const int WmHotKey = 0x0312;
    public const int SwRestore = 9;
    public const uint ModAlt = 0x0001;
    public const uint ModShift = 0x0004;
    public const uint ModControl = 0x0002;
    public const uint ModWin = 0x0008;
    public const uint WdaExcludeFromCapture = 0x00000011;

    private const int DwmwaExtendedFrameBounds = 9;

    public delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

    public static IReadOnlyList<IntPtr> EnumerateTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        _ = EnumWindows((hwnd, _) =>
        {
            windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public static bool TryEnablePerMonitorV2DpiAwareness()
    {
        return SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    public static IReadOnlyList<WindowSnapshot> EnumerateCapturableWindows()
    {
        return EnumerateTopLevelWindows()
            .Where(IsUsableCaptureTarget)
            .Select(GetWindowSnapshot)
            .Where(static window =>
                !string.IsNullOrWhiteSpace(window.Title) ||
                !string.IsNullOrWhiteSpace(window.ProcessName))
            .ToList();
    }

    public static bool IsVisibleWindow(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
    }

    public static bool IsUsableCaptureTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        CaptureBounds bounds = GetWindowBounds(hwnd);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public static WindowSnapshot GetWindowSnapshot(IntPtr hwnd)
    {
        int processId = GetProcessId(hwnd);
        string processName = GetProcessName(processId);
        string title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = string.IsNullOrWhiteSpace(processName) ? "Untitled window" : processName;
        }

        return new WindowSnapshot(hwnd, title, processName, processId, GetWindowBounds(hwnd));
    }

    public static int GetProcessId(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out uint processId);
        return unchecked((int)processId);
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public static CaptureBounds GetWindowBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out Rect rect, Marshal.SizeOf<Rect>()) != 0 ||
            rect.Width <= 0 ||
            rect.Height <= 0)
        {
            if (!GetWindowRect(hwnd, out rect))
            {
                return new CaptureBounds(0, 0, 0, 0);
            }
        }

        return new CaptureBounds(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    private static string GetProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
}
