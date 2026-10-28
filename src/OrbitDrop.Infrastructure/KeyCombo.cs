namespace OrbitDrop.Infrastructure;

/// <summary>
/// Key combinations as text ("Ctrl+Alt+Space", "Ctrl+Shift+F5") and as RegisterHotKey modifiers and virtual keys. A
/// combination needs Ctrl or Alt (so typing never triggers it) and one key: A–Z, 0–9, F1–F24 or Space.
/// </summary>
public static class KeyCombo
{
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;
    public const uint Space = 0x20;

    /// <summary>The shortcut that opens the wheel for the selected files, until the user picks another.</summary>
    public const string DefaultWheel = "Ctrl+Alt+Space";

    public static bool TryParse(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0; key = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string part = raw.ToUpperInvariant();
            switch (part)
            {
                case "CTRL" or "CONTROL": modifiers |= Control; continue;
                case "ALT": modifiers |= Alt; continue;
                case "SHIFT": modifiers |= Shift; continue;
                case "WIN" or "WINDOWS": modifiers |= Win; continue;
            }
            if (key != 0) return false; // two keys
            if (part.Length == 1 && (char.IsAsciiLetterUpper(part[0]) || char.IsAsciiDigit(part[0]))) key = part[0];
            else if (part.Length is 2 or 3 && part[0] == 'F' && int.TryParse(part[1..], out int f) && f is >= 1 and <= 24) key = (uint)(0x70 + f - 1);
            else if (part == "SPACE") key = Space;
            else return false;
        }
        return key != 0 && (modifiers & (Control | Alt)) != 0;
    }

    /// <summary>The canonical spelling: "Ctrl+Alt+Shift+Win+J".</summary>
    public static string Format(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & Control) != 0) parts.Add("Ctrl");
        if ((modifiers & Alt) != 0) parts.Add("Alt");
        if ((modifiers & Shift) != 0) parts.Add("Shift");
        if ((modifiers & Win) != 0) parts.Add("Win");
        parts.Add(key == Space ? "Space" : key is >= 0x70 and <= 0x87 ? $"F{key - 0x70 + 1}" : ((char)key).ToString());
        return string.Join("+", parts);
    }

    /// <summary>The canonical spelling of <paramref name="text"/>, or null when it isn't a usable combination.</summary>
    public static string? Normalize(string? text) => TryParse(text, out uint m, out uint k) ? Format(m, k) : null;

    /// <summary>For people: "Ctrl + Alt + Space".</summary>
    public static string Display(string text) => text.Replace("+", " + ");
}
