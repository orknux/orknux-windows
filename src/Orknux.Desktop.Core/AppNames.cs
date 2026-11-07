namespace Orknux.Desktop.Core;

/// <summary>
/// Which installed app a person means by the name they would type into the
/// Start menu: "musigium", "notepad++", "musescore".
///
/// Windows starts a bare name only when it is a program on the PATH, so asked
/// to open "musigium" it answered that it could not find the file, though the
/// app sat in the Start menu. The names are the Start menu's - its shortcuts
/// and its list of installed apps - and this picks one the way a person
/// would: the whole name first, then a name that starts with it, then one with
/// it as a word, then anywhere; never an uninstaller or a readme when the app
/// itself is there.
/// </summary>
public static class AppNames
{
    private static readonly string[] Incidental = ["uninstall", "setup", "readme", "help", "documentation", "release notes", "website", "license", "repair"];

    /// <summary>The best of <paramref name="names"/> for <paramref name="asked"/>, or null when none contains it.</summary>
    public static string? Best(string asked, IEnumerable<string> names)
    {
        var wanted = Normal(asked);
        if (wanted.Length == 0) return null;
        return names
            .Select(name => (name, rank: Rank(wanted, Normal(name))))
            .Where(one => one.rank < int.MaxValue)
            .OrderBy(one => one.rank)
            .ThenBy(one => one.name.Length)
            .ThenBy(one => one.name, StringComparer.OrdinalIgnoreCase)
            .Select(one => one.name)
            .FirstOrDefault();
    }

    /// <summary>A few names that share a word with <paramref name="asked"/>, for saying what there is when nothing matched.</summary>
    public static IReadOnlyList<string> Near(string asked, IEnumerable<string> names, int most = 5)
    {
        var words = Normal(asked).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length >= 3).ToArray();
        return names
            .Where(name => words.Any(word => Normal(name).Contains(word[..Math.Min(4, word.Length)], StringComparison.Ordinal)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name.Length)
            .Take(most)
            .ToList();
    }

    private static int Rank(string wanted, string name)
    {
        if (name.Length == 0) return int.MaxValue;
        var penalty = Incidental.Any(word => name.Contains(word, StringComparison.Ordinal) && !wanted.Contains(word, StringComparison.Ordinal)) ? 10 : 0;
        if (name == wanted) return 0 + penalty;
        if (name.StartsWith(wanted + " ", StringComparison.Ordinal) || name.StartsWith(wanted, StringComparison.Ordinal)) return 1 + penalty;
        if ((" " + name + " ").Contains(" " + wanted + " ", StringComparison.Ordinal)) return 2 + penalty;
        if (name.Contains(wanted, StringComparison.Ordinal)) return 3 + penalty;
        return int.MaxValue;
    }

    /// <summary>
    /// Whether a running program, by its process name ("Musigium", "MuseScore4",
    /// "notepad"), is the app <paramref name="asked"/> names: the same name, or the
    /// name with a version number after it. Strict on purpose - "notepad" must not
    /// find Notepad++ already open and decide Notepad is running.
    /// </summary>
    public static bool IsProgram(string asked, string? program)
    {
        if (string.IsNullOrWhiteSpace(program)) return false;
        var wanted = Normal(asked).Replace(" ", "");
        var running = Normal(program).Replace(" ", "");
        if (wanted.Length == 0) return false;
        return running == wanted || (running.StartsWith(wanted, StringComparison.Ordinal) && running[wanted.Length..].All(char.IsDigit));
    }

    /// <summary>Lower case, one space between words, and no ".lnk" or ".exe".</summary>
    public static string Normal(string text)
    {
        var trimmed = text.Trim();
        foreach (var ending in new[] { ".lnk", ".exe", ".appref-ms" })
            if (trimmed.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^ending.Length];
        return string.Join(' ', trimmed.ToLowerInvariant().Split((char[])[' ', '\t', '_', '-'], StringSplitOptions.RemoveEmptyEntries));
    }
}
