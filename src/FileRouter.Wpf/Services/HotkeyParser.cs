using System.Windows.Input;

namespace FileRouter.Wpf.Services;

/// <summary>Parses the config's free-text route hotkeys ("Ctrl+1", "F2",
/// "Ctrl+Shift+M") into real key gestures. The WinForms app hardwired
/// Ctrl+1-9 and treated the config field as a label; here the field binds.</summary>
public static class HotkeyParser
{
    public static bool TryParse(string? text, out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        var token = parts[^1];
        if (token.Length == 1 && token[0] is >= '0' and <= '9')
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }
        return Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None;
    }

    /// <summary>A bindable gesture, or null when the text is blank/invalid or
    /// not gesture-capable (bare letters/digits need a modifier in WPF).</summary>
    public static KeyGesture? ToGesture(string? text)
    {
        if (!TryParse(text, out var mods, out var key)) return null;
        try
        {
            return new KeyGesture(key, mods);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Human-readable form of a gesture ("Ctrl+1", "F2"). Numpad
    /// digits display like top-row ones — the runtime binds both, and showing
    /// one name keeps duplicate detection honest ("Ctrl+NumPad1" IS "Ctrl+1").</summary>
    public static string Display(KeyGesture gesture)
    {
        var parts = new List<string>();
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(gesture.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (gesture.Key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (gesture.Key - Key.NumPad0))).ToString(),
            _ => gesture.Key.ToString(),
        });
        return string.Join("+", parts);
    }

    /// <summary>What the shell's fixed key bindings mean, or null when the
    /// combination is free. A route hotkey must not claim one of these — its
    /// binding registers later and would silently shadow the app action.</summary>
    public static string? ReservedUse(ModifierKeys mods, Key key) => (mods, key) switch
    {
        (ModifierKeys.Control, Key.K) => "Set aside",
        (ModifierKeys.Control | ModifierKeys.Shift, Key.Z) => "Undo",
        (ModifierKeys.Control, Key.OemComma) => "Settings",
        _ => null,
    };

    /// <summary>The numpad twin of a top-row digit (and vice versa), or None.
    /// WPF gestures match exact keys; users expect either "1" to work.</summary>
    public static Key DigitTwin(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => Key.NumPad0 + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => Key.D0 + (key - Key.NumPad0),
        _ => Key.None,
    };
}
