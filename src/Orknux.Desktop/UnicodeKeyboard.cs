using System.Runtime.InteropServices;

namespace Orknux.Desktop;

/// <summary>
/// Types text as the characters themselves rather than as the keys that would
/// make them.
///
/// FlaUI's <c>Keyboard.Type(string)</c> turns each character into a virtual key
/// plus Shift, or AltGr where the layout needs it, so what arrives depends on
/// the keyboard layout and on whatever modifier is down at that moment: a
/// Polish letter is Ctrl+Alt plus a key, and a test typed "Hello from Orknux"
/// that arrived as "Hello FROM Orknux". A Unicode keystroke carries the
/// character and nothing else, so no layout and no modifier changes it.
/// Newlines and tabs are sent as the Enter and Tab keys, which is what a text
/// box expects of them.
/// </summary>
internal static class UnicodeKeyboard
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // The union's widest member is MOUSEINPUT; the size has to match it or SendInput refuses every event.
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] private MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public static void Type(string text)
    {
        var events = new List<INPUT>(text.Length * 2);
        foreach (var unit in text.Replace("\r\n", "\n"))
        {
            switch (unit)
            {
                case '\n': Key(events, VK_RETURN); break;
                case '\t': Key(events, VK_TAB); break;
                default:
                    // A UTF-16 unit at a time: a character outside the basic plane is its two surrogates, as Windows expects.
                    events.Add(Event(0, unit, KEYEVENTF_UNICODE));
                    events.Add(Event(0, unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
                    break;
            }
        }
        // One character at a time, with a breath between. Sent as one burst, Windows 11 Notepad kept the first
        // few characters of each batch and dropped the rest: "Hello from Orknux - zażółć gęślą jaźń" arrived as
        // "Hello aźń". A person types slower than this, and every editor keeps up with a person.
        for (var i = 0; i < events.Count; i += 2)
        {
            SendInput(2, [events[i], events[i + 1]], Marshal.SizeOf<INPUT>());
            Thread.Sleep(KeyGapMs);
        }
    }

    /** The pause after each character: 37 characters take a third of a second, a page of text half a minute. */
    private const int KeyGapMs = 8;

    private static void Key(List<INPUT> events, ushort key)
    {
        events.Add(Event(key, '\0', 0));
        events.Add(Event(key, '\0', KEYEVENTF_KEYUP));
    }

    private static INPUT Event(ushort key, char unit, uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = key, wScan = unit, dwFlags = flags } } };
}
