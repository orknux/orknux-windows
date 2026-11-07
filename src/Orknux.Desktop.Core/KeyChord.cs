namespace Orknux.Desktop.Core;

/// <summary>
/// A key or a combination as a model writes it - "Enter", "Ctrl+S", "Ctrl+Shift+N",
/// "Alt+F4", "Win+R" - read into the modifiers held and the one key pressed.
/// Names are matched without regard to case and spelled back canonically, so the
/// Windows side maps a fixed vocabulary to virtual keys and nothing else.
/// </summary>
public sealed record KeyChord(IReadOnlyList<string> Modifiers, string Key)
{
    private static readonly Dictionary<string, string> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = "Ctrl", ["control"] = "Ctrl",
        ["shift"] = "Shift",
        ["alt"] = "Alt", ["option"] = "Alt",
        ["win"] = "Win", ["windows"] = "Win", ["meta"] = "Win", ["cmd"] = "Win",
    };

    /// <summary>The named keys, spelled as the Windows side expects them.</summary>
    public static readonly IReadOnlyList<string> NamedKeys =
    [
        "Enter", "Tab", "Escape", "Backspace", "Delete", "Insert", "Space",
        "Up", "Down", "Left", "Right", "PageUp", "PageDown", "Home", "End",
        "PrintScreen", "Menu",
        .. Enumerable.Range(1, 24).Select(n => "F" + n),
    ];

    private static readonly Dictionary<string, string> KeyNames = BuildKeyNames();

    private static Dictionary<string, string> BuildKeyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in NamedKeys) names[key] = key;
        names["Return"] = "Enter";
        names["Esc"] = "Escape";
        names["Del"] = "Delete";
        names["Ins"] = "Insert";
        names["ArrowUp"] = "Up";
        names["ArrowDown"] = "Down";
        names["ArrowLeft"] = "Left";
        names["ArrowRight"] = "Right";
        names["PgUp"] = "PageUp";
        names["PgDn"] = "PageDown";
        names["Spacebar"] = "Space";
        return names;
    }

    /// <summary>Reads "Ctrl+Shift+S"; throws <see cref="RefusedException"/> for anything it does not know.</summary>
    public static KeyChord Parse(string? written)
    {
        var text = (written ?? "").Trim();
        if (text.Length == 0) throw new RefusedException("Say which key to press, like Enter or Ctrl+S.");
        // "+" alone, or a chord ending in "+" ("Ctrl++"), is the plus key.
        var parts = new List<string>();
        foreach (var part in text.Split('+'))
        {
            if (part.Length == 0) continue;
            parts.Add(part.Trim());
        }
        if (text.EndsWith('+')) parts.Add("+");
        if (parts.Count == 0) throw new RefusedException($"\"{text}\" is not a key.");

        var modifiers = new List<string>();
        foreach (var part in parts.Take(parts.Count - 1))
        {
            if (!ModifierNames.TryGetValue(part, out var modifier))
                throw new RefusedException($"\"{part}\" is not a modifier. Use Ctrl, Shift, Alt or Win, joined with + like Ctrl+Shift+S.");
            if (!modifiers.Contains(modifier)) modifiers.Add(modifier);
        }

        var last = parts[^1];
        string key;
        if (KeyNames.TryGetValue(last, out var named)) key = named;
        else if (last.Length == 1) key = char.IsLetter(last[0]) ? last.ToUpperInvariant() : last;
        else if (ModifierNames.TryGetValue(last, out var alone) && modifiers.Count == 0) key = alone;
        else
            throw new RefusedException(
                $"\"{last}\" is not a key this knows. Use one character, a name like Enter, Tab, Escape, an arrow, PageUp or F5, or a combination like Ctrl+S.");
        return new KeyChord(modifiers, key);
    }

    public override string ToString() => string.Join("+", Modifiers.Append(Key));
}
