using System.Windows.Input;

namespace SoundMatrix;

/// <summary>A key plus modifiers. Stored as e.g. "Ctrl+Alt+M", displayed as e.g. "Ctrl+Alt+M" / "=".</summary>
public readonly record struct Hotkey(Key Key, ModifierKeys Modifiers)
{
    public bool IsEmpty => Key == Key.None;

    public bool Matches(Key key, ModifierKeys modifiers) => !IsEmpty && key == Key && modifiers == Modifiers;

    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var mods = ModifierKeys.None;
        var key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default:
                    if (Enum.TryParse<Key>(part, true, out var k)) key = k;
                    break;
            }
        }
        return new Hotkey(key, mods);
    }

    public string Serialize() => IsEmpty ? "" : string.Join("+", ModifierNames().Append(Key.ToString()));

    public string Display => IsEmpty ? "Unassigned" : string.Join("+", ModifierNames().Append(KeyName(Key)));

    public override string ToString() => Display;

    IEnumerable<string> ModifierNames()
    {
        if (Modifiers.HasFlag(ModifierKeys.Control)) yield return "Ctrl";
        if (Modifiers.HasFlag(ModifierKeys.Alt)) yield return "Alt";
        if (Modifiers.HasFlag(ModifierKeys.Shift)) yield return "Shift";
        if (Modifiers.HasFlag(ModifierKeys.Windows)) yield return "Win";
    }

    public static string KeyName(Key key) => key switch
    {
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Space => "Space",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Left => "←",
        Key.Right => "→",
        Key.Prior => "PgUp",
        Key.Next => "PgDn",
        Key.Back => "Backspace",
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(key - Key.NumPad0),
        _ => key.ToString(),
    };

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    /// <summary>1–9 from the top row or numpad, otherwise null.</summary>
    public static int? Digit(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };
}
