using System.Text;
using Winshots.App.Windows;

namespace Winshots.App.Settings;

public sealed record HotkeyBinding(uint Modifiers, Keys Key)
{
    public static HotkeyBinding DefaultCapture { get; } = new(NativeMethods.ModControl | NativeMethods.ModShift, Keys.Space);

    public static HotkeyBinding DefaultCaptureToCodex { get; } = new(NativeMethods.ModControl | NativeMethods.ModShift, Keys.Enter);

    public static bool TryParseOptional(string? value, out HotkeyBinding? hotkey, out string error)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "None", StringComparison.OrdinalIgnoreCase))
        {
            hotkey = null;
            error = string.Empty;
            return true;
        }

        return TryParse(value, out hotkey, out error);
    }

    public static bool TryParse(string value, out HotkeyBinding? hotkey, out string error)
    {
        hotkey = null;
        error = string.Empty;

        string[] parts = value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            error = "Shortcut is empty.";
            return false;
        }

        uint modifiers = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!TryParseModifier(parts[i], out uint modifier))
            {
                error = $"Unknown modifier '{parts[i]}'.";
                return false;
            }

            modifiers |= modifier;
        }

        if (modifiers == 0)
        {
            error = "Shortcut must include at least one modifier.";
            return false;
        }

        if (!TryParseKey(parts[^1], out Keys key))
        {
            error = $"Unknown key '{parts[^1]}'.";
            return false;
        }

        hotkey = new HotkeyBinding(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        AppendModifier(builder, Modifiers, NativeMethods.ModControl, "Ctrl");
        AppendModifier(builder, Modifiers, NativeMethods.ModAlt, "Alt");
        AppendModifier(builder, Modifiers, NativeMethods.ModShift, "Shift");
        AppendModifier(builder, Modifiers, NativeMethods.ModWin, "Win");

        if (builder.Length > 0)
        {
            builder.Append('+');
        }

        builder.Append(Key == Keys.Return ? Keys.Enter : Key);
        return builder.ToString();
    }

    private static void AppendModifier(StringBuilder builder, uint modifiers, uint value, string label)
    {
        if ((modifiers & value) == value)
        {
            if (builder.Length > 0)
            {
                builder.Append('+');
            }

            builder.Append(label);
        }
    }

    private static bool TryParseModifier(string value, out uint modifier)
    {
        modifier = value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => NativeMethods.ModControl,
            "ALT" => NativeMethods.ModAlt,
            "SHIFT" => NativeMethods.ModShift,
            "WIN" or "WINDOWS" => NativeMethods.ModWin,
            _ => 0
        };

        return modifier != 0;
    }

    private static bool TryParseKey(string value, out Keys key)
    {
        key = value.ToUpperInvariant() switch
        {
            "ESC" => Keys.Escape,
            "RETURN" => Keys.Enter,
            _ => 0
        };

        if (key == 0 && !Enum.TryParse(value, ignoreCase: true, out key))
        {
            return false;
        }

        return key is not Keys.ControlKey
            and not Keys.ShiftKey
            and not Keys.Menu
            and not Keys.LWin
            and not Keys.RWin
            and not Keys.None;
    }
}
