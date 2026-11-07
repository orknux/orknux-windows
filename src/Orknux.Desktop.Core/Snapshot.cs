using System.Text;
using System.Text.Json.Nodes;

namespace Orknux.Desktop.Core;

/// <summary>
/// One element of a window as UI Automation describes it, reduced to what a model
/// reads: its role, its name, its value, the states worth saying, and - for a
/// control - the ref a click names it by.
/// </summary>
public sealed class SnapNode
{
    public required string Role { get; init; }
    public string Name { get; init; } = "";
    public string? Value { get; init; }
    public IReadOnlyList<string> States { get; init; } = [];
    /// <summary>Set for a control a tool can act on; null for text and containers.</summary>
    public string? Ref { get; set; }
    public List<SnapNode> Children { get; } = [];
}

/// <summary>
/// The refs of the latest snapshot of a window: "e1", "e2", ... in reading order,
/// each standing for the element it was handed out for. A new snapshot replaces
/// the table, so a ref from an older one is refused rather than resolved to
/// whatever now sits at that number.
/// </summary>
public sealed class RefTable<T> where T : class
{
    private readonly Dictionary<string, T> byRef = new(StringComparer.OrdinalIgnoreCase);
    private int next = 1;

    public string Add(T element)
    {
        var name = "e" + next++;
        byRef[name] = element;
        return name;
    }

    public T Resolve(string? reference)
    {
        var text = (reference ?? "").Trim();
        if (text.Length == 0) throw new RefusedException("Give the control's ref from desktop_snapshot, like e12.");
        if (!byRef.TryGetValue(text, out var element))
            throw new RefusedException($"\"{text}\" is not a ref in the latest snapshot of this window. Take a fresh desktop_snapshot.");
        return element;
    }

    public int Count => byRef.Count;
}

/// <summary>Writes a window out as the lines desktop_snapshot answers.</summary>
public static class SnapshotRenderer
{
    private const int LongestText = 300;

    /// <summary>The roles that are controls: given a ref, and their own text is their name.</summary>
    public static readonly IReadOnlySet<string> Controls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "button", "edit", "combobox", "checkbox", "radiobutton", "hyperlink", "listitem", "menuitem",
        "tabitem", "treeitem", "slider", "spinner", "splitbutton", "dataitem", "menubar", "scrollbar",
        "document", "headeritem",
    };

    public static IReadOnlyList<string> LinesOf(SnapNode root, bool interactiveOnly)
    {
        var lines = new List<string>();
        Visit(root, quiet: false);
        return lines;

        void Visit(SnapNode node, bool quiet)
        {
            var hush = quiet;
            var name = Flat(node.Name);
            if (node.Ref is not null)
            {
                var line = new StringBuilder($"[{node.Ref}] {node.Role.ToLowerInvariant()}");
                if (name.Length > 0) line.Append(' ').Append(Quoted(name));
                var value = Flat(node.Value);
                if (value.Length > 0 && value != name) line.Append(" value=").Append(Quoted(value));
                if (node.States.Count > 0) line.Append(" (").Append(string.Join(", ", node.States)).Append(')');
                lines.Add(line.ToString());
                hush = true;
            }
            else if (!interactiveOnly && !quiet && name.Length > 0 &&
                     (node.Role.Equals("text", StringComparison.OrdinalIgnoreCase) || node.Role.Equals("image", StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add(node.Role.Equals("image", StringComparison.OrdinalIgnoreCase) ? "image " + Quoted(name) : Cut(name));
            }
            else if (!interactiveOnly && name.Length > 0 && node.Role.Equals("header", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("heading " + Quoted(name));
                hush = true;
            }
            foreach (var child in node.Children) Visit(child, hush);
        }
    }

    /// <summary>
    /// The answer: a heading naming the window, then its lines, narrowed by
    /// <paramref name="find"/> and cut at <paramref name="maxCharacters"/> on a line's end.
    /// </summary>
    public static JsonObject Render(long window, string title, IReadOnlyList<string> lines, string? find, int maxCharacters)
    {
        var shown = lines;
        if (!string.IsNullOrEmpty(find))
            shown = lines.Where(line => line.Contains(find, StringComparison.OrdinalIgnoreCase)).ToList();
        var text = new StringBuilder($"Window: {(title.Length > 0 ? title : "(untitled)")}\nId: {window}\n");
        var kept = 0;
        foreach (var line in shown)
        {
            if (text.Length + line.Length + 1 > maxCharacters) break;
            text.Append(line).Append('\n');
            kept++;
        }
        var result = new JsonObject { ["window"] = window, ["title"] = title, ["text"] = text.ToString() };
        if (kept < shown.Count)
            result["cut"] = $"{shown.Count - kept} more lines were cut. Ask for a part with find, take interactiveOnly, or scroll.";
        if (!string.IsNullOrEmpty(find) && shown.Count == 0) result["note"] = $"Nothing in the window contains \"{find}\".";
        return result;
    }

    private static string Flat(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Cut(string text) => text.Length > LongestText ? text[..LongestText] + "…" : text;

    private static string Quoted(string text) => "\"" + Cut(text).Replace("\"", "\\\"") + "\"";
}
