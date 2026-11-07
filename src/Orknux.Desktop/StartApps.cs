using Orknux.Desktop.Core;

namespace Orknux.Desktop;

/// <summary>
/// The apps the Start menu offers, by name, and how to start each: its
/// shortcuts, from the person's and the machine's Start menu folders, and the
/// installed apps Windows lists in shell:AppsFolder - which is where an app
/// from the Microsoft Store is, with no shortcut file at all.
///
/// Read when a bare name would not start, never before: listing them takes a
/// moment, and a name on the PATH needs none of it. Must run on an STA thread,
/// as the shell's objects require; UiaRunner's is.
/// </summary>
internal static class StartApps
{
    /** Names to how each starts: a shortcut's path, or shell:AppsFolder\<id>. */
    public static Dictionary<string, string> All()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                 })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    found.TryAdd(Path.GetFileNameWithoutExtension(shortcut), shortcut);
            }
            catch (Exception) { /* a folder this account may not read is left out, not the whole menu */ }
        }
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is not null)
            {
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    string name = item.Name;
                    string id = item.Path;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) found.TryAdd(name, @"shell:AppsFolder\" + id);
                }
            }
        }
        catch (Exception) { /* without the shell's list the shortcuts still answer */ }
        return found;
    }

    /** How to start what a person called [asked], with the name it goes by, or null; [near] holds names to offer instead. */
    public static (string Name, string Start)? Find(string asked, out IReadOnlyList<string> near)
    {
        var all = All();
        near = [];
        if (AppNames.Best(asked, all.Keys) is { } best) return (best, all[best]);
        near = AppNames.Near(asked, all.Keys);
        return null;
    }
}
