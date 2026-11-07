using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Orknux.Desktop.Core;

namespace Orknux.Desktop;

/// <summary>
/// What each desktop_* tool does, through UI Automation (FlaUI over UIA3) and the
/// real mouse and keyboard - what a screen reader reads and what a person does.
///
/// Everything runs on one STA thread (<see cref="StaWorker"/>), because UIA's
/// elements belong to the apartment that found them. The refs of the latest
/// snapshot of each window are kept here; a click names one, and a ref from an
/// older snapshot is refused rather than resolved to whatever moved into its place.
/// </summary>
internal sealed class UiaRunner : ICommandRunner, IDisposable
{
    private const int MostNodes = 4_000;
    private const int DeepestNode = 40;
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(350);

    private readonly StaWorker sta = new();
    private readonly UIA3Automation automation;
    private readonly Dictionary<long, RefTable<AutomationElement>> refs = new();
    private long? lastWindow;

    public UiaRunner()
    {
        automation = sta.Invoke(() => new UIA3Automation());
    }

    public JsonNode Run(string action, JsonObject args) => sta.Invoke<JsonNode>(() => action switch
    {
        "windows" => Windows(),
        "open" => Open(args),
        "focus" => Focus(args),
        "snapshot" => Snapshot(args),
        "click" => Click(args),
        "type" => Type(args),
        "press" => Press(args),
        "scroll" => Scroll(args),
        "screenshot" => Screenshot(args),
        "close" => Close(args),
        _ => throw new RefusedException($"Orknux for Windows does not know the action \"{action}\". It may be older than the server."),
    });

    // ---- the tools ----

    private JsonNode Windows()
    {
        var foreground = GetForegroundWindow();
        var list = new JsonArray();
        foreach (var window in TopWindows())
        {
            var handle = Handle(window);
            list.Add(new JsonObject
            {
                ["window"] = handle,
                ["title"] = window.Name,
                ["program"] = ProgramOf(window),
                ["focused"] = handle == foreground.ToInt64(),
                ["minimized"] = window.Patterns.Window.PatternOrDefault?.WindowVisualState.ValueOrDefault == WindowVisualState.Minimized,
                ["last"] = handle == lastWindow,
            });
        }
        return new JsonObject { ["windows"] = list };
    }

    private JsonNode Open(JsonObject args)
    {
        var target = args.Text("target") ?? throw new RefusedException("Say what to open: a program, a file, a folder or an address.");
        var before = TopWindows().Select(Handle).ToHashSet();
        Process? started;
        try
        {
            started = Process.Start(new ProcessStartInfo(target, args.Text("arguments") ?? "") { UseShellExecute = true });
        }
        catch (Exception failed)
        {
            throw new RefusedException($"Windows could not open \"{target}\": {failed.Message}");
        }
        // A new window, or failing that the one that came to the front: many programs hand off to a running copy.
        var until = DateTime.UtcNow.AddSeconds(15);
        AutomationElement? opened = null;
        while (DateTime.UtcNow < until && opened is null)
        {
            Thread.Sleep(250);
            opened = TopWindows().FirstOrDefault(w => !before.Contains(Handle(w)) &&
                (started is null || SafeProcessId(w) == SafeId(started) || !string.IsNullOrEmpty(w.Name)));
        }
        opened ??= WindowOf(GetForegroundWindow().ToInt64());
        if (opened is null) return new JsonObject { ["opened"] = target, ["note"] = "It started, but no window appeared yet. List the windows to find it." };
        lastWindow = Handle(opened);
        Wait.UntilResponsive(opened, TimeSpan.FromSeconds(5));
        return Described(opened);
    }

    private JsonNode Focus(JsonObject args)
    {
        var window = WindowArg(args, required: true);
        Front(window);
        return Described(window);
    }

    private JsonNode Snapshot(JsonObject args)
    {
        var window = WindowArg(args);
        var handle = Handle(window);
        var table = new RefTable<AutomationElement>();
        var walker = automation.TreeWalkerFactory.GetControlViewWalker();
        var count = 0;
        var root = Node(window, table, walker, 0, ref count);
        refs[handle] = table;
        lastWindow = handle;
        var lines = SnapshotRenderer.LinesOf(root, args.Flag("interactiveOnly"));
        var answer = SnapshotRenderer.Render(handle, window.Name ?? "", lines, args.Text("find"), args.Number("maxCharacters") ?? 20_000);
        if (count >= MostNodes) answer["note"] = $"The window has more than {MostNodes} elements; only the first were read. Use find, or focus on one part.";
        return answer;
    }

    private JsonNode Click(JsonObject args)
    {
        var button = (args.Text("button") ?? "left").ToLowerInvariant() switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            var other => throw new RefusedException($"\"{other}\" is not a mouse button. Use left, right or middle."),
        };
        Point point;
        string clicked;
        if (args.Text("ref") is { } reference)
        {
            var (window, element) = Element(args, reference);
            Front(window);
            element.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
            point = PointOf(element);
            clicked = reference;
        }
        else if (args.Number("x") is { } x && args.Number("y") is { } y)
        {
            point = new Point(x, y);
            clicked = $"{x},{y}";
        }
        else
        {
            throw new RefusedException("Give the control's ref from desktop_snapshot, or x and y in screen pixels.");
        }
        if (args.Flag("double")) Mouse.DoubleClick(point, button);
        else Mouse.Click(point, button);
        Settled();
        return new JsonObject { ["clicked"] = clicked, ["at"] = new JsonArray(point.X, point.Y), ["foreground"] = Described(WindowOf(GetForegroundWindow().ToInt64())) };
    }

    private JsonNode Type(JsonObject args)
    {
        var text = args.Text("text") ?? "";
        if (args.Text("ref") is { } reference)
        {
            var (window, element) = Element(args, reference);
            Front(window);
            try { element.Focus(); }
            catch (Exception) { Mouse.Click(PointOf(element)); }
            if (!args.Flag("append"))
            {
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Keyboard.Type(VirtualKeyShort.DELETE);
            }
        }
        // Named a window and no element: that window in front first, as a key press does. Without it the
        // text went to whatever had the focus - a window the person had just clicked into, in one test.
        else if (args.Number("window") is not null) Front(WindowArg(args));
        if (text.Length > 0) UnicodeKeyboard.Type(text);
        if (args.Flag("submit")) Keyboard.Type(VirtualKeyShort.ENTER);
        Settled();
        return new JsonObject { ["typed"] = args.Text("ref") ?? "where the focus was", ["submitted"] = args.Flag("submit") };
    }

    private JsonNode Press(JsonObject args)
    {
        var chord = KeyChord.Parse(args.Text("keys"));
        if (args.Number("window") is not null) Front(WindowArg(args));
        var keys = chord.Modifiers.Select(Modifier).Append(KeyOf(chord.Key)).ToArray();
        if (keys.Length == 1) Keyboard.Type(keys[0]);
        else Keyboard.TypeSimultaneously(keys);
        Settled();
        return new JsonObject { ["pressed"] = chord.ToString() };
    }

    private JsonNode Scroll(JsonObject args)
    {
        var amount = Math.Clamp(args.Number("amount") ?? 5, 1, 50);
        Point point;
        if (args.Text("ref") is { } reference)
        {
            var (window, element) = Element(args, reference);
            Front(window);
            point = PointOf(element);
        }
        else
        {
            var window = WindowArg(args);
            Front(window);
            point = Center(window.BoundingRectangle);
        }
        Mouse.MoveTo(point);
        switch ((args.Text("direction") ?? "down").ToLowerInvariant())
        {
            case "down": Mouse.Scroll(-amount); break;
            case "up": Mouse.Scroll(amount); break;
            case "left": Mouse.HorizontalScroll(-amount); break;
            case "right": Mouse.HorizontalScroll(amount); break;
            case var other: throw new RefusedException($"\"{other}\" is not a direction. Use down, up, left or right.");
        }
        Settled();
        return new JsonObject { ["scrolled"] = args.Text("direction") ?? "down", ["notches"] = amount };
    }

    private JsonNode Screenshot(JsonObject args)
    {
        CaptureImage image;
        string title;
        if (args.Flag("screen"))
        {
            image = Capture.Screen();
            title = "The whole screen";
        }
        else
        {
            var window = WindowArg(args);
            Front(window);
            image = Capture.Element(window);
            title = window.Name ?? "";
        }
        using (image)
        using (var bytes = new MemoryStream())
        {
            var jpeg = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            using var quality = new EncoderParameters(1);
            quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
            image.Bitmap.Save(bytes, jpeg, quality);
            return new JsonObject
            {
                ["data"] = Convert.ToBase64String(bytes.ToArray()),
                ["mimeType"] = "image/jpeg",
                ["title"] = title,
                ["width"] = image.Bitmap.Width,
                ["height"] = image.Bitmap.Height,
            };
        }
    }

    private JsonNode Close(JsonObject args)
    {
        var window = WindowArg(args, required: true);
        var handle = Handle(window);
        window.AsWindow().Close();
        refs.Remove(handle);
        if (lastWindow == handle) lastWindow = null;
        Settled();
        return new JsonObject { ["closed"] = handle, ["stillOpen"] = WindowOf(handle) is not null };
    }

    // ---- the parts ----

    private SnapNode Node(AutomationElement element, RefTable<AutomationElement> table, FlaUI.Core.ITreeWalker walker, int depth, ref int count)
    {
        count++;
        var type = SafeControlType(element);
        var role = type.ToString().ToLowerInvariant();
        var node = new SnapNode
        {
            Role = role,
            Name = Safe(() => element.Name) ?? "",
            Value = Safe(() => element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault),
            States = StatesOf(element),
        };
        if (SnapshotRenderer.Controls.Contains(role) && Try(() => element.IsEnabled) is not null) node.Ref = table.Add(element);
        if (depth >= DeepestNode) return node;
        var child = Safe(() => walker.GetFirstChild(element));
        while (child is not null && count < MostNodes)
        {
            if (Try(() => child.IsOffscreen) != true) node.Children.Add(Node(child, table, walker, depth + 1, ref count));
            child = Safe(() => walker.GetNextSibling(child));
        }
        return node;
    }

    private static List<string> StatesOf(AutomationElement element)
    {
        var states = new List<string>();
        if (Try(() => element.IsEnabled) == false) states.Add("disabled");
        if (Try(() => element.Properties.HasKeyboardFocus.ValueOrDefault) == true) states.Add("focused");
        var toggle = Try(() => element.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault);
        if (toggle == ToggleState.On) states.Add("checked");
        if (toggle == ToggleState.Indeterminate) states.Add("mixed");
        if (Try(() => element.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault) == true) states.Add("selected");
        var expand = Try(() => element.Patterns.ExpandCollapse.PatternOrDefault?.ExpandCollapseState.ValueOrDefault);
        if (expand == ExpandCollapseState.Expanded) states.Add("expanded");
        if (expand == ExpandCollapseState.Collapsed) states.Add("collapsed");
        return states;
    }

    private IEnumerable<AutomationElement> TopWindows() =>
        automation.GetDesktop().FindAllChildren(cf => cf.ByControlType(ControlType.Window))
            .Where(w => Try(() => w.IsOffscreen) != true && !string.IsNullOrWhiteSpace(Safe(() => w.Name)) && Handle(w) != OwnWindow());

    private AutomationElement WindowArg(JsonObject args, bool required = false)
    {
        long? id = args.Number("window") is { } given ? given : (required ? null : lastWindow ?? GetForegroundWindow().ToInt64());
        if (id is null) throw new RefusedException("Say which window, by its id from desktop_windows.");
        var window = WindowOf(id.Value) ?? throw new RefusedException($"There is no window {id} any more. List the windows again.");
        if (Handle(window) == OwnWindow()) throw new RefusedException("That is Orknux for Windows' own window, which the agent may not drive.");
        return window;
    }

    private (AutomationElement Window, AutomationElement Element) Element(JsonObject args, string reference)
    {
        var window = WindowArg(args);
        if (!refs.TryGetValue(Handle(window), out var table))
            throw new RefusedException("This window has not been read yet. Take a desktop_snapshot first.");
        var element = table.Resolve(reference);
        if (Try(() => element.BoundingRectangle) is null)
            throw new RefusedException($"{reference} is no longer on the screen - the window changed since the snapshot. Take a fresh desktop_snapshot.");
        return (window, element);
    }

    private AutomationElement? WindowOf(long handle)
    {
        if (handle == 0) return null;
        try { return automation.FromHandle(new IntPtr(handle)); }
        catch (Exception) { return null; }
    }

    private void Front(AutomationElement window)
    {
        var pattern = window.Patterns.Window.PatternOrDefault;
        if (pattern?.WindowVisualState.ValueOrDefault == WindowVisualState.Minimized)
            pattern.SetWindowVisualState(WindowVisualState.Normal);
        try { window.AsWindow().SetForeground(); } catch (Exception) { /* a window that refuses still takes clicks */ }
        lastWindow = Handle(window);
        Wait.UntilInputIsProcessed();
        // Asked to the front is not yet in front: keys sent at once went to the window before it, and a Ctrl+T
        // for Notepad opened nothing. Wait until Windows says it is the foreground window, then a moment more
        // for it to give the keyboard focus to its own control.
        var handle = new IntPtr(lastWindow ?? 0);
        for (var waited = 0; waited < ForegroundWaitMs && GetForegroundWindow() != handle; waited += 25) Thread.Sleep(25);
        Thread.Sleep(ActivatedMs);
    }

    /** How long a window is given to come to the front, and to settle once it has. */
    private const int ForegroundWaitMs = 1000;
    private const int ActivatedMs = 120;

    private JsonObject Described(AutomationElement? window) => window is null
        ? new JsonObject()
        : new JsonObject { ["window"] = Handle(window), ["title"] = window.Name, ["program"] = ProgramOf(window) };

    private static Point PointOf(AutomationElement element) =>
        element.TryGetClickablePoint(out var point) ? point : Center(element.BoundingRectangle);

    private static Point Center(Rectangle box) => new(box.Left + box.Width / 2, box.Top + box.Height / 2);

    private static void Settled()
    {
        Wait.UntilInputIsProcessed();
        Thread.Sleep(Settle);
    }

    private static long Handle(AutomationElement element) =>
        Try(() => element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64()) ?? 0;

    private static string? ProgramOf(AutomationElement window)
    {
        try { return Process.GetProcessById(window.Properties.ProcessId.ValueOrDefault).ProcessName; }
        catch (Exception) { return null; }
    }

    private static int? SafeProcessId(AutomationElement window) => Try(() => window.Properties.ProcessId.ValueOrDefault);

    private static int? SafeId(Process process)
    {
        try { return process.Id; } catch (Exception) { return null; }
    }

    private static ControlType SafeControlType(AutomationElement element)
    {
        try { return element.ControlType; } catch (Exception) { return ControlType.Custom; }
    }

    /// <summary>An element can go between finding it and reading it; that is "not there", not a fault.</summary>
    private static T? Safe<T>(Func<T?> read) where T : class
    {
        try { return read(); } catch (Exception) { return null; }
    }

    /// <summary>The same for a value: null where it could not be read.</summary>
    private static T? Try<T>(Func<T> read) where T : struct
    {
        try { return read(); } catch (Exception) { return null; }
    }

    /// <summary>The same for a value that may already be absent.</summary>
    private static T? Try<T>(Func<T?> read) where T : struct
    {
        try { return read(); } catch (Exception) { return null; }
    }

    private static long OwnWindow() => Process.GetCurrentProcess().MainWindowHandle.ToInt64();

    private static VirtualKeyShort Modifier(string name) => name switch
    {
        "Ctrl" => VirtualKeyShort.CONTROL,
        "Shift" => VirtualKeyShort.SHIFT,
        "Alt" => VirtualKeyShort.ALT,
        "Win" => VirtualKeyShort.LWIN,
        _ => throw new RefusedException($"\"{name}\" is not a modifier."),
    };

    private static VirtualKeyShort KeyOf(string key)
    {
        switch (key)
        {
            case "Enter": return VirtualKeyShort.ENTER;
            case "Tab": return VirtualKeyShort.TAB;
            case "Escape": return VirtualKeyShort.ESCAPE;
            case "Backspace": return VirtualKeyShort.BACK;
            case "Delete": return VirtualKeyShort.DELETE;
            case "Insert": return VirtualKeyShort.INSERT;
            case "Space": return VirtualKeyShort.SPACE;
            case "Up": return VirtualKeyShort.UP;
            case "Down": return VirtualKeyShort.DOWN;
            case "Left": return VirtualKeyShort.LEFT;
            case "Right": return VirtualKeyShort.RIGHT;
            case "PageUp": return VirtualKeyShort.PRIOR;
            case "PageDown": return VirtualKeyShort.NEXT;
            case "Home": return VirtualKeyShort.HOME;
            case "End": return VirtualKeyShort.END;
            case "PrintScreen": return VirtualKeyShort.SNAPSHOT;
            case "Menu": return VirtualKeyShort.APPS;
            case "Ctrl": return VirtualKeyShort.CONTROL;
            case "Shift": return VirtualKeyShort.SHIFT;
            case "Alt": return VirtualKeyShort.ALT;
            case "Win": return VirtualKeyShort.LWIN;
        }
        if (key.Length > 1 && key[0] == 'F' && int.TryParse(key[1..], out var n) && n is >= 1 and <= 24)
            return (VirtualKeyShort)((int)VirtualKeyShort.F1 + n - 1);
        if (key.Length == 1)
        {
            var c = key[0];
            if (c is >= 'A' and <= 'Z') return (VirtualKeyShort)c;
            if (c is >= '0' and <= '9') return (VirtualKeyShort)c;
            var scan = VkKeyScan(c);
            if (scan != -1) return (VirtualKeyShort)(scan & 0xFF);
        }
        throw new RefusedException($"\"{key}\" cannot be pressed on this keyboard.");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern short VkKeyScan(char ch);

    public void Dispose()
    {
        sta.Invoke(() => { automation.Dispose(); return 0; });
        sta.Dispose();
    }
}
